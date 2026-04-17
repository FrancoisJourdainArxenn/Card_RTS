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
    [ServerRpc(RequireOwnership = false)]
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
    [ServerRpc(RequireOwnership = false)]
    public void PlayCreatureServerRpc(int cardUniqueID, int tablePos, int baseID, int playerIndex)
    {
        int creatureUniqueID = IDFactory.GetUniqueID();
        Debug.Log($"[GameNetworkManager] PlayCreature : joueur {playerIndex}, carte {cardUniqueID}, créature {creatureUniqueID}");
        PlayCreatureClientRpc(playerIndex, cardUniqueID, creatureUniqueID, tablePos, baseID);
    }

    /// <summary>
    /// Reçu par TOUS les clients : exécute la logique + le visuel de jouer une créature
    /// avec les mêmes identifiants sur toutes les machines.
    /// </summary>
    [ClientRpc]
    void PlayCreatureClientRpc(int playerIndex, int cardUniqueID, int creatureUniqueID, int tablePos, int baseID)
    {
        if (Player.Players == null || playerIndex < 0 || playerIndex >= Player.Players.Length)
        {
            Debug.LogError($"[GameNetworkManager] PlayCreatureClientRpc : playerIndex {playerIndex} invalide");
            return;
        }
        Player player = Player.Players[playerIndex];
        player.NetworkPlayCreatureFromHand(cardUniqueID, creatureUniqueID, tablePos, baseID);
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
    [ServerRpc(RequireOwnership = false)]
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

    [ServerRpc(RequireOwnership = false)]
    public void MoveCreatureServerRpc(int creatureUniqueID, int targetBaseID, int tablePos)
    {
        MoveCreatureClientRpc(creatureUniqueID, targetBaseID, tablePos);
    }

    /// <summary>
    /// Reçu par TOUS les clients : exécute le déplacement avec les mêmes paramètres.
    /// </summary>
    [ClientRpc]
    void MoveCreatureClientRpc(int creatureUniqueID, int targetBaseID, int tablePos)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureUniqueID, out CreatureLogic creature))
        {
            Debug.LogError($"[GameNetworkManager] MoveCreature: créature introuvable id={creatureUniqueID}");
            return;
        }
        creature.Move(targetBaseID, tablePos);
    }

    //Attacking Units

    [ServerRpc(RequireOwnership = false)]
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

    [ServerRpc(RequireOwnership = false)]
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

    [ServerRpc(RequireOwnership = false)]
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
}
