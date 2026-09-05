using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

public class CreatureMoveVisual : MonoBehaviour
{
    private OneCreatureManager manager;
    private WhereIsTheCardOrCreature w;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        manager = GetComponent<OneCreatureManager>();    
        w = GetComponent<WhereIsTheCardOrCreature>();
    }

    public void Move(int baseID, int tablePos)
    {
        IDHolder id = GetComponent<IDHolder>();
        if (id == null || !CreatureLogic.CreaturesCreatedThisGame.ContainsKey(id.UniqueID))
        {
            Debug.LogError("CreatureMoveVisual: creature logic not found.");
            return;
        }

        CreatureLogic creatureLogic = CreatureLogic.CreaturesCreatedThisGame[id.UniqueID];
        Player owner = creatureLogic.owner;
        PlayerArea startingArea = owner.GetPlayerAreaByID(manager.BaseID);
        PlayerArea targetArea = owner.GetPlayerAreaByID(baseID);
        if (targetArea == null)
        {
            Debug.LogError($"CreatureMoveVisual: no PlayerArea found for baseID={baseID} on player {owner.name}");
            return;
        }

        manager.BaseID = baseID;
        GameObject creatureToRemove = IDHolder.GetGameObjectWithID(id.UniqueID);
        startingArea.tableVisual.MoveCreatureAway(creatureToRemove);
        // tablePos arrive en index logique (ghosts exclus, voir TableVisual.ToNetworkTablePos) : ce
        // n'est qu'une position de repli pour l'insertion initiale — MoveCreatureToIndex retrie
        // ensuite la rangée selon le dernier ordre connu (voir TableVisual.ApplyCreatureOrder), qui
        // seul détermine la position finale réelle.
        int rawTablePos = targetArea.tableVisual.FromNetworkTablePos(creatureLogic.IsMelee, tablePos);
        targetArea.tableVisual.MoveCreatureToIndex(gameObject, id.UniqueID, rawTablePos, baseID);

        // Une créature avec Transport débarque automatiquement ses passagers à l'arrivée — voir
        // DisembarkCargo. Ce move est le seul point de passage commun à la résolution solo différée,
        // au réseau (MoveCreatureClientRpc) et à l'immédiat (CreatureLogic.Move appelé directement) :
        // brancher le débarquement ici plutôt que dans chacun des appelants garantit qu'il tourne
        // partout, une seule fois.
        if (creatureLogic.BoardedCreatureIDs.Count > 0)
        {
            //Debug.Log($"[Transport] Move — {creatureLogic.DisplayName}(ID:{id.UniqueID}) arrived at baseID={baseID} carrying {creatureLogic.BoardedCreatureIDs.Count} passenger(s), disembarking...");
            DisembarkCargo(creatureLogic, startingArea, targetArea);
        }
    }

    // Embarque cette créature à bord de transportCreatureID : retirée de sa rangée et masquée (elle
    // réapparaîtra via Disembark, portée par le transporteur). Voir CreatureLogic.Board.
    public void Board(int transportCreatureID)
    {
        IDHolder id = GetComponent<IDHolder>();
        if (id == null || !CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id.UniqueID, out CreatureLogic creatureLogic))
        {
            Debug.LogError("CreatureMoveVisual: creature logic not found (Board).");
            return;
        }

        PlayerArea startingArea = creatureLogic.owner.GetPlayerAreaByID(manager.BaseID);
        startingArea?.tableVisual.MoveCreatureAway(gameObject);
        gameObject.SetActive(false);
        // Résolu : le flag de protection (voir Player.HighlightPlayableCards) ne sert plus qu'à
        // couvrir la fenêtre "en attente" — sans ce reset, une créature réembarquée resterait assombrie
        // pour toujours après son prochain débarquement (Disembark réactive le même GameObject).
        manager.HasPendingBoard = false;
        manager.PendingBoardTarget = null;
        //Debug.Log($"[Transport] Visual.Board — {creatureLogic.DisplayName}(ID:{id.UniqueID}) removed from row and hidden (GameObject.activeSelf={gameObject.activeSelf})");

        IDHolder.GetGameObjectWithID(transportCreatureID)?.GetComponent<OneCreatureManager>()?.RefreshPassengerPortraits();
    }

    // Débarque cette créature (embarquée) dans la rangée de baseID à tablePos (index réseau,
    // ghost-free — voir CreatureLogic.DisembarkAt). Réactive le GameObject masqué depuis Board().
    public void Disembark(int baseID, int tablePos)
    {
        IDHolder id = GetComponent<IDHolder>();
        if (id == null || !CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id.UniqueID, out CreatureLogic creatureLogic))
        {
            Debug.LogError("CreatureMoveVisual: creature logic not found (Disembark).");
            return;
        }

        Player owner = creatureLogic.owner;
        PlayerArea targetArea = owner.GetPlayerAreaByID(baseID);
        if (targetArea == null)
        {
            Debug.LogError($"CreatureMoveVisual: no PlayerArea found for baseID={baseID} on player {owner.name} (Disembark)");
            return;
        }

        manager.BaseID = baseID;
        gameObject.SetActive(true);
        int rawTablePos = targetArea.tableVisual.FromNetworkTablePos(creatureLogic.IsMelee, tablePos);
        //Debug.Log($"[Transport] Visual.Disembark — {creatureLogic.DisplayName}(ID:{id.UniqueID}) reactivated into baseID={baseID}, rawTablePos={rawTablePos}");
        targetArea.tableVisual.MoveCreatureToIndex(gameObject, id.UniqueID, rawTablePos, baseID);
    }

    // Fin de phase Command pour un transport qui n'a pas changé de zone ce tour — tente quand même
    // de débarquer ses passagers dans sa zone actuelle (voir TurnManager.ResolveStationaryTransportDisembarks).
    // Contrairement à DisembarkCargo appelé depuis Move (où origin != dest), un passager qui ne
    // rentre pas ici reste simplement à bord (pas de zone de départ où le "laisser derrière").
    public void DisembarkCargoInPlace()
    {
        IDHolder id = GetComponent<IDHolder>();
        if (id == null || !CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id.UniqueID, out CreatureLogic creatureLogic))
            return;
        if (creatureLogic.BoardedCreatureIDs.Count == 0)
            return;

        PlayerArea area = creatureLogic.owner.GetPlayerAreaByID(manager.BaseID);
        if (area == null)
            return;

        //Debug.Log($"[Transport] DisembarkCargoInPlace — {creatureLogic.DisplayName}(ID:{id.UniqueID}) stayed put with {creatureLogic.BoardedCreatureIDs.Count} passenger(s), attempting in-place disembark...");
        DisembarkCargo(creatureLogic, area, area, inPlace: true);
    }

    // Débarque tous les passagers de carrier à son arrivée dans destArea — dans l'ordre du manifeste
    // (voir CreatureLogic.ManifestOrder), gauche/droite du transport respecté pour ceux de son propre
    // type mêlée/distance, sinon simplement à la suite (pas de "côté" du transport dans l'autre
    // rangée). Chacun dans sa propre rangée si la place le permet, sinon laissé derrière dans
    // originArea. Compte réel (hors ghosts, voir TableVisual.ToNetworkTablePos) : les ghosts de
    // déplacement en attente sont locaux à un seul client et ne doivent JAMAIS influencer une décision
    // qui doit être identique sur toutes les machines (contrairement à la vérification de place faite
    // une seule fois, côté client, au moment de la commande — voir DragCreatureActions.Move).
    private void DisembarkCargo(CreatureLogic carrier, PlayerArea originArea, PlayerArea destArea, bool inPlace = false)
    {
        List<int> manifest = new List<int>(carrier.ManifestOrder);
        int maxPerRow = GlobalSettings.Instance.MaxCreaturePerRow;
        //Debug.Log($"[Transport] DisembarkCargo — {carrier.DisplayName}(ID:{carrier.UniqueCreatureID}) processing manifest=[{string.Join(", ", manifest)}], origin baseID={originArea.baseID}, dest baseID={destArea.baseID}, maxPerRow={maxPerRow}, inPlace={inPlace}");

        int landed = DisembarkRow(carrier, manifest, true, originArea, destArea, maxPerRow, inPlace)
                   + DisembarkRow(carrier, manifest, false, originArea, destArea, maxPerRow, inPlace);

        int totalPassengers = manifest.Count - (manifest.Contains(carrier.UniqueCreatureID) ? 1 : 0);
        if (totalPassengers > landed && !inPlace)
            new ShowMessageCommand("Not all transported units could reach that zone.", 1f).AddToQueue();

        //Debug.Log($"[Transport] DisembarkCargo — done: {landed}/{totalPassengers} landed, {totalPassengers - landed} {(inPlace ? "still boarded" : "left behind")}");
    }

    // Débarque, dans la rangée mêlée ou distance (isMelee) de destArea, tous les passagers du
    // manifeste partageant ce type — ceux placés avant le transport dans le manifeste atterrissent à
    // sa gauche (indices juste avant sa position déjà fixée par CreatureMoveVisual.Move, qui place le
    // transporteur avant d'appeler DisembarkCargo), ceux après à sa droite. Un passager d'un type
    // différent du transporteur n'a pas de "côté" à respecter : simple suite, comme avant. Retourne le
    // nombre de passagers ayant effectivement atterri (les autres sont laissés derrière à originArea).
    private int DisembarkRow(CreatureLogic carrier, List<int> manifest, bool isMelee, PlayerArea originArea, PlayerArea destArea, int maxPerRow, bool inPlace)
    {
        List<int> sameTypeIDs = new List<int>();
        int carrierPos = -1;
        foreach (int id in manifest)
        {
            if (id == carrier.UniqueCreatureID)
            {
                if (carrier.IsMelee == isMelee)
                    carrierPos = sameTypeIDs.Count;
                continue;
            }
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic passenger)) continue;
            if (passenger.IsMelee != isMelee) continue;
            sameTypeIDs.Add(id);
        }
        if (sameTypeIDs.Count == 0) return 0;

        List<GameObject> destRow = isMelee ? destArea.tableVisual.MeleeCreaturesOnTable : destArea.tableVisual.RangedCreaturesOnTable;
        int destRawCountBefore = destRow.Count;
        int destOccupied = destArea.tableVisual.ToNetworkTablePos(isMelee, destRawCountBefore);

        GameObject carrierGO = IDHolder.GetGameObjectWithID(carrier.UniqueCreatureID);
        int carrierRawIndex = (carrierPos >= 0 && carrierGO != null) ? destRow.IndexOf(carrierGO) : -1;

        // Étape 1 : qui atterrit (place disponible) vs qui reste derrière, dans l'ordre du manifeste —
        // même priorité "premier arrivé, premier servi" qu'avant, juste relue depuis le manifeste.
        List<int> landingLeft = new List<int>();
        List<int> landingRight = new List<int>();
        int landed = 0;
        for (int i = 0; i < sameTypeIDs.Count; i++)
        {
            int passengerID = sameTypeIDs[i];
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(passengerID, out CreatureLogic passenger)) continue;

            if (destOccupied + landed < maxPerRow)
            {
                landed++;
                bool isLeft = carrierRawIndex >= 0 && i < carrierPos;
                if (isLeft) landingLeft.Add(passengerID);
                else landingRight.Add(passengerID);
                //Debug.Log($"[Transport] DisembarkRow — {passenger.DisplayName}(ID:{passengerID}) FITS ({(isLeft ? "left" : "right")} of carrier, isMelee={isMelee})");
            }
            else if (inPlace)
            {
                //Debug.Log($"[Transport] DisembarkRow — {passenger.DisplayName}(ID:{passengerID}) STAYS BOARDED (zone full: {destOccupied + landed}/{maxPerRow}, isMelee={isMelee})");
            }
            else
            {
                //Debug.Log($"[Transport] DisembarkRow — {passenger.DisplayName}(ID:{passengerID}) LEFT BEHIND at origin (destination row full: {destOccupied + landed}/{maxPerRow}, isMelee={isMelee})");
                int originRaw = isMelee ? originArea.tableVisual.MeleeCreaturesOnTable.Count : originArea.tableVisual.RangedCreaturesOnTable.Count;
                int originReal = originArea.tableVisual.ToNetworkTablePos(isMelee, originRaw);
                passenger.DisembarkAt(originArea.baseID, originReal);
            }
        }

        // Étape 2 : position finale — forme fermée autour de la position déjà fixée du transporteur :
        // landingLeft[j] prend l'indice carrierRawIndex+j, landingRight[k] prend
        // carrierRawIndex+landingLeft.Count+1+k. Si le transporteur n'est pas de ce type, tout le
        // monde s'ajoute simplement à la suite (comportement identique à l'ancien code).
        // Simulation locale de destRow : CreatureLogic.DisembarkAt() ne l'insère jamais tout de
        // suite (la CreatureDisembarkCommand créée est seulement mise en file — voir
        // Command.AddToQueueImmediate, playingQueue déjà vrai pendant ce Move). Sans cette
        // simulation, ToNetworkTablePos relirait la même rangée non modifiée pour chaque passager
        // et les ferait tous cibler le même index réseau.
        List<GameObject> simulatedRow = new List<GameObject>(destRow);
        int baseRaw = carrierRawIndex >= 0 ? carrierRawIndex : destRawCountBefore;
        for (int j = 0; j < landingLeft.Count; j++)
        {
            int networkTarget = SimulatedNetworkPos(simulatedRow, baseRaw + j);
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(landingLeft[j], out CreatureLogic p))
            {
                p.DisembarkAt(destArea.baseID, networkTarget);
                simulatedRow.Insert(Mathf.Min(baseRaw + j, simulatedRow.Count), IDHolder.GetGameObjectWithID(landingLeft[j]));
            }
        }
        int rightStart = baseRaw + landingLeft.Count + (carrierRawIndex >= 0 ? 1 : 0);
        for (int k = 0; k < landingRight.Count; k++)
        {
            int networkTarget = SimulatedNetworkPos(simulatedRow, rightStart + k);
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(landingRight[k], out CreatureLogic p))
            {
                p.DisembarkAt(destArea.baseID, networkTarget);
                simulatedRow.Insert(Mathf.Min(rightStart + k, simulatedRow.Count), IDHolder.GetGameObjectWithID(landingRight[k]));
            }
        }

        return landed;
    }

    // Équivalent local de TableVisual.ToNetworkTablePos, mais sur une rangée simulée en mémoire —
    // voir le commentaire dans DisembarkRow ci-dessus pour pourquoi destRow elle-même ne peut pas
    // être relue directement entre deux passagers de la même boucle.
    private static int SimulatedNetworkPos(List<GameObject> row, int rawVisualIndex)
    {
        int logical = 0;
        for (int i = 0; i < rawVisualIndex && i < row.Count; i++)
            if (!TableVisual.IsGhost(row[i])) logical++;
        return logical;
    }
}
