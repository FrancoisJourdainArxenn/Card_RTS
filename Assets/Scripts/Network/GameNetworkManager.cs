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
        TurnManager.Instance.RegisterEndPhase(playerIndex);
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
    }

    public void BroadCastDrawCard(int playerIndex)
    {
        int cardID = IDFactory.GetUniqueID();
        DrawAcardClientRpc(playerIndex, cardID);
        Debug.Log($"[GameNetworkManager] BroadCastDrawCard : joueur {playerIndex} doit piocher carte {cardID}");
    }

    [ClientRpc]
    public void DrawAcardClientRpc(int playerIndex, int cardID)
    {
        Player player = Player.Players[playerIndex];
        player.DrawACard(false, cardID);
        Debug.Log($"[GameNetworkManager] DrawAcardClientRpc : joueur {playerIndex} reçoit carte {cardID}");
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
    public void AttackBuildingServerRpc(int attackerID, int targetBuildingID)
    {
        AttackBuildingClientRpc(attackerID, targetBuildingID);
    }

    [ClientRpc]
    void AttackBuildingClientRpc(int attackerID, int targetBuildingID)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic attacker))
        {
            Debug.LogError($"[GameNetworkManager] AttackBuilding: attaquant introuvable id={attackerID}");
            return;
        }
        attacker.AttackBuildingWithID(targetBuildingID);
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
        int buildingUniqueID = IDFactory.GetUniqueID();
        BuildNeutralBaseClientRpc(playerIndex, neutralBaseId, buildingUniqueID);
    }

    [ClientRpc]
    void BuildNeutralBaseClientRpc(int playerIndex, int neutralBaseId, int buildingUniqueID)
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
        player.ExecuteBuildNeutralBase(neutralBaseVisual, buildingUniqueID);
    }


}
