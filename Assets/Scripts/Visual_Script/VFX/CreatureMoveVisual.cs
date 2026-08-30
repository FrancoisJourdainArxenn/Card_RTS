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
            Debug.Log($"[Transport] Move — {creatureLogic.DisplayName}(ID:{id.UniqueID}) arrived at baseID={baseID} carrying {creatureLogic.BoardedCreatureIDs.Count} passenger(s), disembarking...");
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
        Debug.Log($"[Transport] Visual.Board — {creatureLogic.DisplayName}(ID:{id.UniqueID}) removed from row and hidden (GameObject.activeSelf={gameObject.activeSelf})");

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
        Debug.Log($"[Transport] Visual.Disembark — {creatureLogic.DisplayName}(ID:{id.UniqueID}) reactivated into baseID={baseID}, rawTablePos={rawTablePos}");
        targetArea.tableVisual.MoveCreatureToIndex(gameObject, id.UniqueID, rawTablePos, baseID);
    }

    // Débarque tous les passagers de carrier à son arrivée dans destArea — dans l'ordre d'embarquement
    // (FIFO), chacun dans sa propre rangée mêlée/distance si la place le permet, sinon laissé derrière
    // dans originArea (sa zone de départ). Compte réel (hors ghosts, voir TableVisual.ToNetworkTablePos)
    // : les ghosts de déplacement en attente sont locaux à un seul client et ne doivent JAMAIS influencer
    // une décision qui doit être identique sur toutes les machines (contrairement à la vérification de
    // place faite une seule fois, côté client, au moment de la commande — voir DragCreatureActions.Move).
    private void DisembarkCargo(CreatureLogic carrier, PlayerArea originArea, PlayerArea destArea)
    {
        List<int> passengers = new List<int>(carrier.BoardedCreatureIDs);
        int meleeUsed = 0, rangedUsed = 0;
        int maxPerRow = GlobalSettings.Instance.MaxCreaturePerRow;
        Debug.Log($"[Transport] DisembarkCargo — {carrier.DisplayName}(ID:{carrier.UniqueCreatureID}) processing {passengers.Count} passenger(s) FIFO order=[{string.Join(", ", passengers)}], origin baseID={originArea.baseID}, dest baseID={destArea.baseID}, maxPerRow={maxPerRow}");

        foreach (int passengerID in passengers)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(passengerID, out CreatureLogic passenger)) continue;
            bool isMelee = passenger.IsMelee;
            int usedSoFar = isMelee ? meleeUsed : rangedUsed;

            int destRaw = isMelee ? destArea.tableVisual.MeleeCreaturesOnTable.Count : destArea.tableVisual.RangedCreaturesOnTable.Count;
            int destReal = destArea.tableVisual.ToNetworkTablePos(isMelee, destRaw);

            if (destReal + usedSoFar < maxPerRow)
            {
                Debug.Log($"[Transport] DisembarkCargo — {passenger.DisplayName}(ID:{passengerID}) FITS at destination (row occupancy {destReal + usedSoFar}/{maxPerRow}, isMelee={isMelee})");
                passenger.DisembarkAt(destArea.baseID, destReal + usedSoFar);
                if (isMelee) meleeUsed++; else rangedUsed++;
            }
            else
            {
                Debug.Log($"[Transport] DisembarkCargo — {passenger.DisplayName}(ID:{passengerID}) LEFT BEHIND at origin (destination row full: {destReal + usedSoFar}/{maxPerRow}, isMelee={isMelee})");
                int originRaw = isMelee ? originArea.tableVisual.MeleeCreaturesOnTable.Count : originArea.tableVisual.RangedCreaturesOnTable.Count;
                int originReal = originArea.tableVisual.ToNetworkTablePos(isMelee, originRaw);
                passenger.DisembarkAt(originArea.baseID, originReal);
            }
        }

        if (passengers.Count > (meleeUsed + rangedUsed))
            new ShowMessageCommand("Not all transported units could reach that zone.", 1f).AddToQueue();

        Debug.Log($"[Transport] DisembarkCargo — done: {meleeUsed + rangedUsed}/{passengers.Count} landed at destination, {passengers.Count - (meleeUsed + rangedUsed)} left behind");
        IDHolder.GetGameObjectWithID(carrier.UniqueCreatureID)?.GetComponent<OneCreatureManager>()?.RefreshPassengerPortraits();
    }
}
