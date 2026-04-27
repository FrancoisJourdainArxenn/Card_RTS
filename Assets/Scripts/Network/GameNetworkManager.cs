using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Chef d'orchestre réseau dans la BattleScene.
/// Attend que les deux clients soient prêts, puis lance la partie.
/// Doit être placé sur un GameObject avec un composant NetworkObject dans la BattleScene.
/// </summary>
public class GameNetworkManager : NetworkBehaviour
{
    public static GameNetworkManager Instance { get; private set; }

    // Compteur côté serveur : combien de clients ont signalé qu'ils sont prêts
    private int readyCount = 0;

    public int DeckSeed => deckSeed.Value;
    private NetworkVariable<int> deckSeed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    void Awake()
    {
        Instance = this;

        // Awake s'exécute avant tous les Start() de la scène.
        // On définit IsNetworkSession ici pour que TurnManager.Start() le voie correctement,
        // que ce soit sur le host ou sur le client.
        // En mode local, IsListening == false donc IsNetworkSession reste false.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkSessionData.IsNetworkSession = true;
        }
    }

    // -------------------------------------------------------------------------
    // SYNCHRONISATION BATTLE PHASE — ATTRIBUTIONS DE DÉGÂTS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stocke temporairement l'attribution de dégâts soumise par un joueur.
    /// Le serveur attend les deux soumissions avant de merger et diffuser l'état canonique.
    /// Clé = playerIndex (0 ou 1).
    /// </summary>
    private struct BattleSubmission
    {
        public int[] CreatureIDs,     CreatureDamages;
        public int[] BaseIDs,         BaseDamages;
        public int[] TargetPlayerIDs, PlayerDamages;
        public int[] BuildingIDs,     BuildingDamages;
    }
    private Dictionary<int, BattleSubmission> _battleSubmissions = new Dictionary<int, BattleSubmission>();

    /// <summary>
    /// Reçu par le serveur quand un joueur termine la Battle phase.
    /// Stocke son attribution de dégâts. Quand les deux joueurs ont soumis :
    ///   1. Merge les deux attributions (union sans conflit, chaque joueur contrôle ses propres attaques)
    ///   2. Diffuse l'état canonique via ApplyCanonicalBattleAssignmentClientRpc
    ///   3. Déclenche la transition de phase via ForceRegisterEndPhase
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SubmitBattleAssignmentServerRpc(
        int playerIndex,
        int[] creatureIDs,     int[] creatureDamages,
        int[] baseIDs,         int[] baseDamages,
        int[] targetPlayerIDs, int[] playerDamages,
        int[] buildingIDs,     int[] buildingDamages)
    {
        _battleSubmissions[playerIndex] = new BattleSubmission
        {
            CreatureIDs     = creatureIDs,     CreatureDamages  = creatureDamages,
            BaseIDs         = baseIDs,         BaseDamages      = baseDamages,
            TargetPlayerIDs = targetPlayerIDs, PlayerDamages    = playerDamages,
            BuildingIDs     = buildingIDs,     BuildingDamages  = buildingDamages
        };

        if (_battleSubmissions.Count < 2)
            return; // On attend encore l'autre joueur

        BattleSubmission s0 = _battleSubmissions[0];
        BattleSubmission s1 = _battleSubmissions[1];

        // Union simple : chaque joueur a soumis les dégâts qu'IL inflige (pas de conflit possible)
        ApplyCanonicalBattleAssignmentClientRpc(
            ConcatArrays(s0.CreatureIDs,     s1.CreatureIDs),
            ConcatArrays(s0.CreatureDamages, s1.CreatureDamages),
            ConcatArrays(s0.BaseIDs,         s1.BaseIDs),
            ConcatArrays(s0.BaseDamages,     s1.BaseDamages),
            ConcatArrays(s0.TargetPlayerIDs, s1.TargetPlayerIDs),
            ConcatArrays(s0.PlayerDamages,   s1.PlayerDamages),
            ConcatArrays(s0.BuildingIDs,     s1.BuildingIDs),
            ConcatArrays(s0.BuildingDamages, s1.BuildingDamages)
        );
        _battleSubmissions.Clear();

        // Déclenche la transition de phase maintenant que les deux joueurs ont soumis
        TurnManager.Instance.ForceRegisterEndPhase(0);
        TurnManager.Instance.ForceRegisterEndPhase(1);
    }

    /// <summary>
    /// Reçu par TOUS les clients : remplace les dictionnaires pendingDamage locaux
    /// par l'état canonique du serveur, avant que OnBattlePhaseEnd() ne les lise.
    /// </summary>
    [ClientRpc]
    void ApplyCanonicalBattleAssignmentClientRpc(
        int[] creatureIDs,     int[] creatureDamages,
        int[] baseIDs,         int[] baseDamages,
        int[] targetPlayerIDs, int[] playerDamages,
        int[] buildingIDs,     int[] buildingDamages)
    {
        ZoneCombatResolver.ApplyCanonicalAssignment(
            creatureIDs, creatureDamages, baseIDs, baseDamages,
            targetPlayerIDs, playerDamages, buildingIDs, buildingDamages);
    }

    static int[] ConcatArrays(int[] firstArray, int[] secondArray)
    {
        int[] result = new int[firstArray.Length + secondArray.Length];
        firstArray.CopyTo(result, 0);
        secondArray.CopyTo(result, firstArray.Length);
        return result;
    }

    // -------------------------------------------------------------------------
    // SYNCHRONISATION GLOBALE DE L'ÉTAT DE JEU
    // -------------------------------------------------------------------------

    /// <summary>
    /// Collecte l'état complet de toutes les entités et le diffuse à tous les clients.
    /// Appelé par le serveur après chaque End phase (une fois les dégâts appliqués).
    /// Sert de filet de sécurité contre tout désync résiduel.
    /// </summary>
    void BroadcastFullGameState()
    {
        List<int> creatureIDList     = new List<int>();
        List<int> creatureHealthList = new List<int>();
        List<int> creatureBaseIDList = new List<int>();
        List<int> attacksLeftList    = new List<int>();
        List<int> movementsLeftList  = new List<int>();

        foreach (KeyValuePair<int, CreatureLogic> entry in CreatureLogic.CreaturesCreatedThisGame)
        {
            creatureIDList.Add(entry.Key);
            creatureHealthList.Add(entry.Value.Health);
            creatureBaseIDList.Add(entry.Value.BaseID);
            attacksLeftList.Add(entry.Value.AttacksLeftThisTurn);
            movementsLeftList.Add(entry.Value.MovementsLeftThisTurn);
        }

        List<int> baseIDList     = new List<int>();
        List<int> baseHealthList = new List<int>();

        foreach (KeyValuePair<int, BaseLogic> entry in BaseLogic.BasesCreatedThisGame)
        {
            baseIDList.Add(entry.Key);
            baseHealthList.Add(entry.Value.Health);
        }

        int playerCount = Player.Players.Length;
        int[] playerHealths   = new int[playerCount];
        int[] playerMainRes   = new int[playerCount];
        int[] playerSecondRes = new int[playerCount];

        for (int i = 0; i < playerCount; i++)
        {
            playerHealths[i]   = Player.Players[i].Health;
            playerMainRes[i]   = Player.Players[i].mainRessourceAvailable;
            playerSecondRes[i] = Player.Players[i].secondRessourceAvailable;
        }

        SyncFullGameStateClientRpc(
            creatureIDList.ToArray(), creatureHealthList.ToArray(),
            creatureBaseIDList.ToArray(), attacksLeftList.ToArray(), movementsLeftList.ToArray(),
            baseIDList.ToArray(), baseHealthList.ToArray(),
            playerHealths, playerMainRes, playerSecondRes);
    }

    /// <summary>
    /// Reçu par les clients (pas le serveur) : corrige l'état local pour qu'il corresponde
    /// à l'état autoritaire du serveur. Logge toute correction détectée pour faciliter
    /// le débogage des désynchronisations résiduelles.
    /// Ne déclenche PAS d'événements de mort — sert uniquement à corriger des valeurs.
    /// </summary>
    [ClientRpc]
    void SyncFullGameStateClientRpc(
        int[] creatureIDs,   int[] creatureHealths, int[] creatureBaseIDs,
        int[] attacksLeft,   int[] movementsLeft,
        int[] baseIDs,       int[] baseHealths,
        int[] playerHealths, int[] playerMainRes,   int[] playerSecondRes)
    {
        if (IsServer) return; // Le serveur est la source de vérité

        for (int i = 0; i < creatureIDs.Length; i++)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureIDs[i], out CreatureLogic creature))
                continue;

            if (creature.Health != creatureHealths[i] && creatureHealths[i] > 0)
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} : HP local={creature.Health}, serveur={creatureHealths[i]}. Correction appliquée.");
                creature.Health = creatureHealths[i];
            }
            if (creature.AttacksLeftThisTurn != attacksLeft[i])
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} : AttacksLeft local={creature.AttacksLeftThisTurn}, serveur={attacksLeft[i]}. Correction appliquée.");
                creature.AttacksLeftThisTurn = attacksLeft[i];
            }
            if (creature.MovementsLeftThisTurn != movementsLeft[i])
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} : MovementsLeft local={creature.MovementsLeftThisTurn}, serveur={movementsLeft[i]}. Correction appliquée.");
                creature.MovementsLeftThisTurn = movementsLeft[i];
            }
        }

        for (int i = 0; i < baseIDs.Length; i++)
        {
            if (!BaseLogic.BasesCreatedThisGame.TryGetValue(baseIDs[i], out BaseLogic _base))
                continue;

            if (_base.Health != baseHealths[i] && baseHealths[i] > 0)
            {
                Debug.LogError($"[Desync] Bâtiment {baseIDs[i]} : HP local={_base.Health}, serveur={baseHealths[i]}. Correction appliquée.");
                _base.Health = baseHealths[i];
            }
        }

        for (int i = 0; i < Player.Players.Length; i++)
        {
            Player player = Player.Players[i];

            if (player.Health != playerHealths[i] && playerHealths[i] > 0)
            {
                Debug.LogError($"[Desync] Joueur {i} : HP local={player.Health}, serveur={playerHealths[i]}. Correction appliquée.");
                player.Health = playerHealths[i];
            }
            if (player.mainRessourceAvailable != playerMainRes[i])
            {
                Debug.LogError($"[Desync] Joueur {i} : ressource principale locale={player.mainRessourceAvailable}, serveur={playerMainRes[i]}. Correction appliquée.");
                player.mainRessourceAvailable = playerMainRes[i];
            }
            if (player.secondRessourceAvailable != playerSecondRes[i])
            {
                Debug.LogError($"[Desync] Joueur {i} : ressource secondaire locale={player.secondRessourceAvailable}, serveur={playerSecondRes[i]}. Correction appliquée.");
                player.secondRessourceAvailable = playerSecondRes[i];
            }
        }
    }

    private List<PendingAction> _actionBuffer = new List<PendingAction>();

    private void RegisterAction(PendingAction action)
    {
        _actionBuffer.Add(action);
        Debug.Log($"[Buffer] Action enregistrée : {action.type} par joueur {action.playerIndex} (total={_actionBuffer.Count})");
    }

    // Called when all players have ended their phase
    // Executes all actions in order: Player 0 first, then Player 1
    public void FlushBuffer()
    {
        Debug.Log($"[Buffer] Flush de {_actionBuffer.Count} action(s)");

        // Sort: player 0's actions come before player 1's, preserving relative order within each player
        List<PendingAction> p0Actions = _actionBuffer.FindAll(a => a.playerIndex == 0);
        List<PendingAction> p1Actions = _actionBuffer.FindAll(a => a.playerIndex == 1);

        foreach (PendingAction action in p0Actions) ExecuteAction(action);
        foreach (PendingAction action in p1Actions) ExecuteAction(action);

        _actionBuffer.Clear();
    }

    private void ExecuteAction(PendingAction action)
    {
        switch (action.type)
        {
            case ActionType.PlayCreature:
                PlayCreatureClientRpc(action.playerIndex, action.param1, action.param2, action.param3, action.param4);
                break;
            case ActionType.MoveCreature:
                MoveCreatureClientRpc(action.param1, action.param2, action.param3);
                break;
        }
    }

    /// <summary>
    /// Appelé automatiquement par Netcode quand cet objet est spawné sur le réseau.
    /// Chaque client récupère son LocalClientId et signale au serveur qu'il est prêt.
    /// En mode local, cette méthode n'est jamais appelée.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        // LocalClientId n'est fiable qu'après OnNetworkSpawn, pas dans Awake
        NetworkSessionData.LocalClientId = NetworkManager.Singleton.LocalClientId;
        PlayerReadyServerRpc();
    }

    /// <summary>
    /// Envoyé par chaque client au serveur pour signaler qu'il est prêt.
    /// RequireOwnership = false : n'importe quel client peut appeler ce ServerRpc.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void PlayerReadyServerRpc()
    {
        readyCount++;
        Debug.Log($"[GameNetworkManager] Joueur prêt : {readyCount}/2");

        if (readyCount >= 2)
        {
            deckSeed.Value = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            int[] cardInHandIDs = new int[TurnManager.Instance.initdraw * Player.Players.Length];
            for (int i = 0; i < cardInHandIDs.Length; i++)
            {
                cardInHandIDs[i] = IDFactory.GetUniqueID();
            }
            Debug.Log("[GameNetworkManager] Les deux joueurs sont prêts. Démarrage de la partie.");
            StartGameClientRpc(deckSeed.Value, cardInHandIDs);
        }
    }

    /// <summary>
    /// Envoyé par le serveur à TOUS les clients pour démarrer la partie.
    /// </summary>
    [ClientRpc]
    void StartGameClientRpc(int deckSeed, int[] cardInHandIDs)
    {
        // 1. Assigner le local player
        AssignLocalPlayerControl();

        // 2. Lancer la logique de démarrage (distribution des cartes, ressources, etc.)
        TurnManager.Instance.OnGameStart(deckSeed, cardInHandIDs);

        // 3. Rafraîchir les boutons maintenant que AllowedToControlThisPlayer est correct
        GlobalSettings.Instance.RefreshEndPhaseButtons();
    }

    /// <summary>
    /// Détermine quel joueur la machine locale peut contrôler.
    /// Host (clientId 0) → LowPlayer
    /// Client (clientId 1) → TopPlayer
    /// </summary>
    void AssignLocalPlayerControl()
    {
        if (!NetworkSessionData.IsNetworkSession)
            return;

        GlobalSettings gs = GlobalSettings.Instance;
        bool isHost = NetworkManager.Singleton.LocalClientId == 0;

        Player localPlayer  = isHost ? gs.LowPlayer  : gs.TopPlayer;
        Player remotePlayer = isHost ? gs.TopPlayer  : gs.LowPlayer;

        localPlayer.MainPArea.AllowedToControlThisPlayer  = true;
        remotePlayer.MainPArea.AllowedToControlThisPlayer = false;
        gs.localPlayer = localPlayer;
        FogOfWarManager.Refresh();
        gs.localPlayerHand.owner = localPlayer.MainPArea.owner;
        localPlayer.MainPArea.handVisual = gs.localPlayerHand;
        
        gs.localPlayerDebugText.text = "Local Player: " + localPlayer.name;

        Debug.Log($"[GameNetworkManager] Joueur local : {localPlayer.name} | Joueur distant : {remotePlayer.name}");
    }

    // -------------------------------------------------------------------------
    // ACTIONS DE JEU — JOUER UNE CRÉATURE
    // -------------------------------------------------------------------------

    /// <summary>
    /// Envoyé par un client pour jouer une créature depuis sa main.
    /// Le serveur génère l'ID de la créature (source unique de vérité) et diffuse à tous.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlayCreatureServerRpc(int cardUniqueID, int tablePos, int baseID, int playerIndex)
    {
        int creatureUniqueID = IDFactory.GetUniqueID();

        bool isCelerity = CardLogic.CardsCreatedThisGame.TryGetValue(cardUniqueID, out CardLogic card) && card.ca.Celerity;

        if (isCelerity)
        {
            ImmediatePlayCreatureClientRpc(playerIndex, cardUniqueID, creatureUniqueID, tablePos, baseID);
        }
        else
        {
            RegisterAction(new PendingAction
            {
                type = ActionType.PlayCreature,
                playerIndex = playerIndex,
                param1 = cardUniqueID,
                param2 = creatureUniqueID,
                param3 = tablePos,
                param4 = baseID
            });
            ShowPendingPlayCreatureClientRpc(playerIndex, cardUniqueID, creatureUniqueID, baseID);
        }
    }

    /// <summary>
    /// Reçu par TOUS les clients : exécute la logique + le visuel de jouer une créature
    /// avec les mêmes identifiants sur toutes les machines.
    /// </summary>
    [ClientRpc]
    void ShowPendingPlayCreatureClientRpc(int playerIndex, int cardUniqueID, int creatureUniqueID, int baseID)
    {
        Player player = Player.Players[playerIndex];
        player.NetworkPendingPlayCreature(cardUniqueID, creatureUniqueID, baseID);
    }

    [ClientRpc]
    void ImmediatePlayCreatureClientRpc(int playerIndex, int cardUniqueID, int creatureUniqueID, int tablePos, int baseID)
    {
        Player player = Player.Players[playerIndex];
        player.NetworkPlayCreatureFromHand(cardUniqueID, creatureUniqueID, tablePos, baseID);
    }

    [ClientRpc]
    void PlayCreatureClientRpc(int playerIndex, int cardUniqueID, int creatureUniqueID, int tablePos, int baseID)
    {
        if (Player.Players == null || playerIndex < 0 || playerIndex >= Player.Players.Length)
        {
            Debug.LogError($"[GameNetworkManager] PlayCreatureClientRpc : playerIndex {playerIndex} invalide");
            return;
        }
        Player player = Player.Players[playerIndex];
        player.NetworkFlushPlayCreature(cardUniqueID, creatureUniqueID, tablePos, baseID);
    }

    // -------------------------------------------------------------------------
    // SYNCHRONISATION DES PHASES DE TOUR
    // -------------------------------------------------------------------------

    /// <summary>
    /// Diffusé par le serveur à tous les clients quand un joueur a cliqué "Fin de phase".
    /// Met à jour l'état local phaseReady et grise le bouton correspondant.
    /// </summary>
    [ClientRpc]
    public void SyncPlayerReadyClientRpc(int playerIndex, TurnManager.TurnPhases forPhase)
    {
        // Ignorer les syncs obsolètes (émis par une phase précédente)
        if (TurnManager.Instance.CurrentPhase != forPhase)
            return;
        TurnManager.Instance.SetPlayerReady(playerIndex);
    }

    /// <summary>
    /// Appelé par un client pour signaler au serveur qu'il a terminé la phase.
    /// Le paramètre forPhase permet d'ignorer les requêtes arrivées en retard
    /// (ex : Regroup auto-register qui arrive après que le serveur est passé en Command).
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RegisterEndPhaseServerRpc(int playerIndex, TurnManager.TurnPhases forPhase)
    {
        if (TurnManager.Instance.CurrentPhase != forPhase)
        {
            Debug.Log($"[GameNetworkManager] RegisterEndPhase ignoré : requête pour {forPhase}, phase actuelle {TurnManager.Instance.CurrentPhase}");
            return;
        }
        Debug.Log($"[GameNetworkManager] RegisterEndPhase reçu pour joueur index {playerIndex} (phase {forPhase})");
        // ForceRegisterEndPhase bypasse le check AllowedToControlThisPlayer, qui n'a de sens
        // que côté client (pour décider d'envoyer le RPC), pas côté serveur (qui traite la requête).
        TurnManager.Instance.ForceRegisterEndPhase(playerIndex);
    }

    /// <summary>
    /// Appelé par le serveur (depuis TurnManager.AdvancePhaseWhenAllReady) pour
    /// diffuser la transition de phase à tous les clients.
    /// </summary>
    public void BroadcastPhaseTransition(TurnManager.TurnPhases nextPhase, bool roundEnded, int newRound)
    {
        PhaseTransitionClientRpc(nextPhase, roundEnded, newRound);
    }

    /// <summary>
    /// Reçu par TOUS les clients (y compris le serveur/host) :
    /// applique la fin de round si nécessaire, puis entre dans la nouvelle phase.
    /// </summary>
    [ClientRpc]
    void PhaseTransitionClientRpc(TurnManager.TurnPhases nextPhase, bool roundEnded, int newRound)
    {
        Debug.Log($"[GameNetworkManager] Transition vers {nextPhase} (round {newRound}, finRound={roundEnded})");

        if (roundEnded)
        {
            foreach (Player p in Player.Players)
                p.OnTurnEnd();
        }

        TurnManager.Instance.SetCurrentRound(newRound);
        TurnManager.Instance.EnterPhase(nextPhase);

        // Après la End phase, les dégâts de bataille ont été appliqués (OnBattlePhaseEnd vient d'être appelé).
        // Le serveur diffuse l'état complet pour corriger tout désync résiduel côté client.
        if (nextPhase == TurnManager.TurnPhases.End && IsServer)
            BroadcastFullGameState();
    }

    private int _drawSeedOffset = 0;

    public void InitDrawSeedOffset(int value) { _drawSeedOffset = value; }

    public void BroadCastDrawCard(int playerIndex)
    {
        if (!IsServer) return;
        int cardID = IDFactory.GetUniqueID();
        int finalSeed = DeckSeed + _drawSeedOffset++;
        Debug.Log($"[BroadCastDrawCard] joueur={playerIndex} cardID={cardID} finalSeed={finalSeed}");
        DrawAcardClientRpc(playerIndex, cardID, finalSeed);
    }

    [ClientRpc]
    public void DrawAcardClientRpc(int playerIndex, int cardID, int finalSeed)
    {
        Debug.Log($"[DrawAcardClientRpc] joueur={playerIndex} cardID={cardID} finalSeed={finalSeed}");
        Player player = Player.Players[playerIndex];
        player.DrawACard(false, cardID, finalSeed);
    } 

    //Moving Units
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void MoveCreatureServerRpc(int creatureUniqueID, int targetBaseID, int tablePos, int playerIndex)
    {
        RegisterAction(new PendingAction
        {
            type = ActionType.MoveCreature,
            playerIndex = playerIndex,
            param1 = creatureUniqueID,
            param2 = targetBaseID,
            param3 = tablePos
        });
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CancelMoveCreatureServerRpc(int creatureUniqueID, int playerIndex)
    {
        int removed = _actionBuffer.RemoveAll(a =>
            a.type == ActionType.MoveCreature &&
            a.param1 == creatureUniqueID &&
            a.playerIndex == playerIndex);

        if (removed > 0)
            CancelMoveCreatureClientRpc(creatureUniqueID);
    }

    [ClientRpc]
    void CancelMoveCreatureClientRpc(int creatureUniqueID)
    {
        IDHolder.GetGameObjectWithID(creatureUniqueID)
            ?.GetComponent<OneCreatureManager>()
            ?.ClearPendingMoveArrow();
    }

    /// <summary>
    /// Reçu par TOUS les clients : exécute le déplacement avec les mêmes paramètres.
    /// </summary>
    [ClientRpc]
    void MoveCreatureClientRpc(int creatureUniqueID, int targetBaseID, int tablePos)
    {
        IDHolder.GetGameObjectWithID(creatureUniqueID)?.GetComponent<OneCreatureManager>()?.ClearPendingMoveArrow();

        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureUniqueID, out CreatureLogic creature))
        {
            Debug.LogError($"[GameNetworkManager] MoveCreature: créature introuvable id={creatureUniqueID}");
            return;
        }
        creature.Move(targetBaseID, tablePos);
    }

    //Attacking Units
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AttackCreatureServerRpc(int attackerID, int targetCreatureID)
    {
        AttackCreatureClientRpc(attackerID, targetCreatureID);
    }

    [ClientRpc]
    void AttackCreatureClientRpc(int attackerID, int targetCreatureID)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic attacker))
        {
            Debug.LogError($"[GameNetworkManager] AttackCreature: attaquant introuvable id={attackerID}");
            return;
        }
        attacker.AttackCreatureWithID(targetCreatureID);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AttackBaseServerRpc(int attackerID, int targetBaseID)
    {
        AttackBaseClientRpc(attackerID, targetBaseID);
    }

    [ClientRpc]
    void AttackBaseClientRpc(int attackerID, int targetBaseID)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic attacker))
        {
            Debug.LogError($"[GameNetworkManager] AttackBase: attaquant introuvable id={attackerID}");
            return;
        }
        attacker.AttackBaseWithID(targetBaseID);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void GoFaceServerRpc(int attackerID)
    {
        GoFaceClientRpc(attackerID);
    }

    [ClientRpc]
    void GoFaceClientRpc(int attackerID)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic attacker))
        {
            Debug.LogError($"[GameNetworkManager] GoFace: attaquant introuvable id={attackerID}");
            return;
        }
        attacker.GoFace();
    }

    ///Neutral Bases
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void BuildNeutralBaseServerRpc(int playerIndex, int neutralBaseId)
    {
        int baseUniqueID = IDFactory.GetUniqueID();
        BuildNeutralBaseClientRpc(playerIndex, neutralBaseId, baseUniqueID);
    }

    [ClientRpc]
    void BuildNeutralBaseClientRpc(int playerIndex, int neutralBaseId, int baseUniqueID)
    {
        if (Player.Players == null || playerIndex < 0 || playerIndex >= Player.Players.Length)
        {
            Debug.LogError($"[GameNetworkManager] BuildNeutralBaseClientRpc : playerIndex {playerIndex} invalide");
            return;
        }
        if (!NeutralBaseVisual.Registry.TryGetValue(neutralBaseId, out NeutralBaseVisual neutralBaseVisual))
        {
            Debug.LogError($"[GameNetworkManager] BuildNeutralBaseClientRpc : NeutralBaseVisual introuvable neutralBaseId={neutralBaseId}");
            return;
        }
        Player player = Player.Players[playerIndex];
        player.ExecuteBuildNeutralBase(neutralBaseVisual, baseUniqueID);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlaceBuildingServerRpc(int playerIndex, string buildingName, int spotID)
    {
        int buildingUniqueID = IDFactory.GetUniqueID();
        PlaceBuildingClientRpc(playerIndex, buildingName, spotID, buildingUniqueID);
    }

    [ClientRpc]
    void PlaceBuildingClientRpc(int playerIndex, string buildingName, int spotID, int buildingUniqueID)
    {
        if (!BuildSpotVisual.Registry.TryGetValue(spotID, out BuildSpotVisual spot))
        {
            Debug.LogError($"PlaceBuildingClientRpc: spot not found id={spotID}");
            return;
        }

        Player player = Player.Players[playerIndex];
        CardAsset building = player.deck.FindBuilding(buildingName);
        if (building == null)
        {
            Debug.LogError($"PlaceBuildingClientRpc: building not found name={buildingName}");
            return;
        }

        player.ExecutePlaceBuilding(building, spot, buildingUniqueID);
    }



}
