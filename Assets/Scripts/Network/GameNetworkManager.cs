using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-200)]

/// <summary>
/// Chef d'orchestre réseau dans la BattleScene.
/// Attend que les deux clients soient prêts, puis lance la partie.
/// Doit être placé sur un GameObject avec un composant NetworkObject dans la BattleScene.
/// </summary>
public class GameNetworkManager : NetworkBehaviour
{
    public static GameNetworkManager Instance { get; private set; }
    NetworkVariable<int> mapIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField] MenuRegistry registry;

    private readonly Dictionary<ulong, int> _deckChoices = new();


    // Compteur côté serveur : combien de clients ont signalé qu'ils sont prêts
    private int readyCount = 0;
    private Dictionary<TurnManager.TurnPhases, HashSet<int>> _pendingEndPhase = new();

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
    /// Joueurs ayant confirmé que leur file de commandes locale (animations de combat) a fini
    /// de jouer. Le serveur n'appelle ForceRegisterEndPhase qu'une fois les deux confirmations
    /// reçues — sinon la transition vers EndBattle peut arriver avant la fin des animations et
    /// interrompre en plein vol une commande (ex. BattleCam), gelant la file pour toujours.
    /// </summary>
    private readonly HashSet<int> _battleAnimationsDone = new HashSet<int>();

    /// <summary>
    /// Reçu par le serveur quand un joueur termine la Battle phase.
    /// Stocke la soumission pour compter les deux joueurs. Quand les deux ont soumis :
    ///   1. Sérialise l'état calculé par le serveur (BuildAutoBattleSequence déjà exécuté dans OnBattlePhaseStart)
    ///   2. Diffuse l'état canonique via ApplyCanonicalBattleAssignmentClientRpc
    ///   3. Déclenche la transition de phase via ForceRegisterEndPhase
    /// Les données soumises par les clients sont ignorées — le serveur est la source de vérité.
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
        Debug.Log($"[BattleAssignment][Server] Soumission reçue — joueur {playerIndex} | créatures={creatureIDs.Length} bases={baseIDs.Length} joueurs={targetPlayerIDs.Length} bâtiments={buildingIDs.Length} | total soumis: {_battleSubmissions.Count}/2");

        if (_battleSubmissions.Count < 2)
        {
            Debug.Log($"[BattleAssignment][Server] En attente de l'autre joueur — {_battleSubmissions.Count}/2 soumission(s) reçue(s)");
            return;
        }

        _battleSubmissions.Clear();
        Debug.Log("[BattleAssignment][Server] Les deux joueurs ont soumis — sérialisation de l'état canonique et broadcast");

        ZoneCombatResolver.BattleAssignment canonical = ZoneCombatResolver.SerializeAllAssignments();

        // Contient à la fois les effets OnDeath et OnAttack résolus par anticipation pendant la
        // planification, dans l'ordre chronologique réel — voir ZoneCombatResolver.PredictedTriggerReplay.
        List<ZoneCombatResolver.PredictedTriggerReplay> predictedReplays = ZoneCombatResolver.DrainPredictedTriggerReplays();
        int[] predictedSourceIDs  = new int[predictedReplays.Count];
        int[] predictedEffectIdxs = new int[predictedReplays.Count];
        int[] predictedSeeds      = new int[predictedReplays.Count];
        int[] predictedDeferKeys  = new int[predictedReplays.Count];
        int[] predictedEventSubjectIDs = new int[predictedReplays.Count];
        int[] predictedTargetIDs = new int[predictedReplays.Count];
        for (int i = 0; i < predictedReplays.Count; i++)
        {
            predictedSourceIDs[i]  = predictedReplays[i].SourceCreatureID;
            predictedEffectIdxs[i] = predictedReplays[i].EffectIndex;
            predictedSeeds[i]      = predictedReplays[i].Seed;
            predictedDeferKeys[i]  = predictedReplays[i].DeferKey;
            predictedEventSubjectIDs[i] = predictedReplays[i].EventSubjectID;
            predictedTargetIDs[i]  = predictedReplays[i].TargetID;
        }

        // Tokens créés par un de ces mêmes triggers prédits (TokenGenerationSO, placement ToZone) —
        // rejoués côté client au bon endroit relatif dans la boucle ci-dessous, jamais via un ClientRpc
        // séparé, pour ne pas devancer la trigger qui les a chronologiquement causés — voir
        // ZoneCombatResolver.IsResolvingPredictedTrigger.
        List<ZoneCombatResolver.PredictedTokenSpawn> predictedTokenSpawns = ZoneCombatResolver.DrainPredictedTokenSpawns();
        int[] tokenSpawnSourceIDs   = new int[predictedTokenSpawns.Count];
        int[] tokenSpawnEffectIdxs  = new int[predictedTokenSpawns.Count];
        int[] tokenSpawnPlayerIdxs  = new int[predictedTokenSpawns.Count];
        int[] tokenSpawnCardIDs     = new int[predictedTokenSpawns.Count];
        int[] tokenSpawnCreatureIDs = new int[predictedTokenSpawns.Count];
        int[] tokenSpawnTablePos    = new int[predictedTokenSpawns.Count];
        int[] tokenSpawnBaseIDs     = new int[predictedTokenSpawns.Count];
        int[] tokenSpawnDeferKeys   = new int[predictedTokenSpawns.Count];
        for (int i = 0; i < predictedTokenSpawns.Count; i++)
        {
            tokenSpawnSourceIDs[i]   = predictedTokenSpawns[i].SourceEntityID;
            tokenSpawnEffectIdxs[i]  = predictedTokenSpawns[i].EffectIndex;
            tokenSpawnPlayerIdxs[i]  = predictedTokenSpawns[i].PlayerIndex;
            tokenSpawnCardIDs[i]     = predictedTokenSpawns[i].CardID;
            tokenSpawnCreatureIDs[i] = predictedTokenSpawns[i].CreatureID;
            tokenSpawnTablePos[i]    = predictedTokenSpawns[i].TablePos;
            tokenSpawnBaseIDs[i]     = predictedTokenSpawns[i].BaseID;
            tokenSpawnDeferKeys[i]   = predictedTokenSpawns[i].DeferKey;
        }

        List<ZoneCombatResolver.OnBattleStartReplay> onBattleStartReplays = ZoneCombatResolver.DrainOnBattleStartReplays();
        int[] battleStartZoneKeys   = new int[onBattleStartReplays.Count];
        int[] battleStartSourceIDs  = new int[onBattleStartReplays.Count];
        int[] battleStartIsBuilding = new int[onBattleStartReplays.Count];
        int[] battleStartEffectIdxs = new int[onBattleStartReplays.Count];
        int[] battleStartSeeds      = new int[onBattleStartReplays.Count];
        for (int i = 0; i < onBattleStartReplays.Count; i++)
        {
            battleStartZoneKeys[i]   = onBattleStartReplays[i].ZoneDeferKey;
            battleStartSourceIDs[i]  = onBattleStartReplays[i].SourceID;
            battleStartIsBuilding[i] = onBattleStartReplays[i].IsBuilding ? 1 : 0;
            battleStartEffectIdxs[i] = onBattleStartReplays[i].EffectIndex;
            battleStartSeeds[i]      = onBattleStartReplays[i].Seed;
        }

        ApplyCanonicalBattleAssignmentClientRpc(
            canonical.CreatureIDs,     canonical.CreatureDamages,
            canonical.BaseIDs,         canonical.BaseDamages,
            canonical.TargetPlayerIDs, canonical.PlayerDamages,
            canonical.BuildingIDs,     canonical.BuildingDamages,
            canonical.ResolverP1Pools, canonical.ResolverP2Pools,
            predictedSourceIDs,        predictedEffectIdxs,        predictedSeeds,        predictedDeferKeys,
            predictedEventSubjectIDs,  predictedTargetIDs,
            tokenSpawnSourceIDs,       tokenSpawnEffectIdxs,       tokenSpawnPlayerIdxs,
            tokenSpawnCardIDs,         tokenSpawnCreatureIDs,      tokenSpawnTablePos,
            tokenSpawnBaseIDs,         tokenSpawnDeferKeys,
            battleStartZoneKeys,       battleStartSourceIDs,       battleStartIsBuilding,
            battleStartEffectIdxs,     battleStartSeeds
        );

        ZoneCombatResolver.SerializeAllBattleSteps(
            out int[] stepResolverIdxs, out int[] stepAttackerIDs, out int[] stepIsBuilding,
            out int[] stepTargetIDs,    out int[] stepTargetKinds, out int[] stepDamages,
            out int[] stepOwnerPlayerIDs,
            out int[] stepSecondaryCounts, out int[] stepSecondaryTargetIDs, out int[] stepSecondaryDamages,
            out int[] stepCounterDamages);
        BroadcastBattleStepsClientRpc(
            stepResolverIdxs, stepAttackerIDs, stepIsBuilding,
            stepTargetIDs, stepTargetKinds, stepDamages, stepOwnerPlayerIDs,
            stepSecondaryCounts, stepSecondaryTargetIDs, stepSecondaryDamages,
            stepCounterDamages);

        // La transition vers EndBattle est désormais déclenchée depuis ReportBattleAnimationsDoneServerRpc,
        // une fois que CHAQUE client a confirmé que sa file de commandes locale a fini de jouer les
        // animations de combat (voir BroadcastBattleStepsClientRpc / WaitForBattleAnimationsThenReport).
    }

    /// <summary>
    /// Reçu par TOUS les clients : reconstruit la séquence de combat et enqueue les commandes
    /// d'animation step-by-step (ZoneClashMove puis CreatureAttackCommand / BuildingAttackCommand).
    /// Doit être reçu après ApplyCanonicalBattleAssignmentClientRpc pour que pendingDamage soit set.
    /// </summary>
    [ClientRpc]
    void BroadcastBattleStepsClientRpc(
        int[] resolverIdxs, int[] attackerIDs, int[] isBuilding,
        int[] targetIDs, int[] targetKinds, int[] damages, int[] ownerPlayerIDs,
        int[] secondaryCounts, int[] secondaryTargetIDs, int[] secondaryDamages,
        int[] counterDamages)
    {
        int nCreature = 0, nBuilding = 0, nBase = 0, nPlayer = 0;
        for (int i = 0; i < targetKinds.Length; i++)
        {
            switch (targetKinds[i]) { case 0: nCreature++; break; case 1: nBuilding++; break; case 2: nBase++; break; case 3: nPlayer++; break; }
            // Debug.Log($"  [BroadcastSteps] step[{i}] kind={targetKinds[i]}(0=Créature,1=Bât,2=Base,3=Joueur) resolver={resolverIdxs[i]} attaquant={attackerIDs[i]} cible={targetIDs[i]} dmg={damages[i]}");
        }
        // Debug.Log($"[BroadcastSteps] {resolverIdxs.Length} steps reçus — Créature={nCreature} Bâtiment={nBuilding} Base={nBase} Joueur={nPlayer}");
        ZoneCombatResolver.EnqueueAllReconstructedBattleCommands(
            resolverIdxs, attackerIDs, isBuilding, targetIDs, targetKinds, damages, ownerPlayerIDs,
            secondaryCounts, secondaryTargetIDs, secondaryDamages, counterDamages);
        // Debug.Log($"[BroadcastSteps] EnqueueAllReconstructedBattleCommands terminé — file de commandes: {Command.CommandQueue.Count} en attente, playingQueue={Command.playingQueue}");
        StartCoroutine(WaitForBattleAnimationsThenReport());
    }

    /// <summary>
    /// Attend que la file de commandes locale (animations de combat qu'on vient d'enqueue)
    /// ait fini de jouer, puis prévient le serveur. Tourne sur CHAQUE client, y compris le host.
    /// </summary>
    IEnumerator WaitForBattleAnimationsThenReport()
    {
        yield return null; // laisser EnqueueAllReconstructedBattleCommands démarrer la file (synchrone)
        yield return new WaitWhile(() => Command.playingQueue);

        int localIndex = System.Array.IndexOf(Player.Players, GlobalSettings.Instance.localPlayer);
        if (localIndex < 0)
        {
            Debug.LogWarning("[Battle] WaitForBattleAnimationsThenReport — localIndex introuvable, confirmation ANNULÉE (le serveur restera bloqué à attendre)");
            yield break;
        }
        Debug.Log($"[Battle] Animations locales terminées — joueur {localIndex} confirme au serveur");
        ReportBattleAnimationsDoneServerRpc(localIndex);
    }

    /// <summary>
    /// Reçu par le serveur quand un client (ou le host) a fini de jouer les animations de combat
    /// localement. Une fois les deux joueurs confirmés, déclenche la transition vers EndBattle.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportBattleAnimationsDoneServerRpc(int playerIndex)
    {
        _battleAnimationsDone.Add(playerIndex);
        Debug.Log($"[BattleAssignment][Server] Animations terminées — joueur {playerIndex} | total confirmé: {_battleAnimationsDone.Count}/2");
        if (_battleAnimationsDone.Count < 2)
            return;

        _battleAnimationsDone.Clear();
        TurnManager.Instance.ForceRegisterEndPhase(0);
        TurnManager.Instance.ForceRegisterEndPhase(1);
    }

    /// <summary>
    /// Reçu par TOUS les clients : remplace les dictionnaires pendingDamage locaux et les
    /// valeurs d'overflow (freePool) par l'état canonique du serveur, avant que OnBattlePhaseEnd() ne les lise.
    /// </summary>
    [ClientRpc]
    void ApplyCanonicalBattleAssignmentClientRpc(
        int[] creatureIDs,     int[] creatureDamages,
        int[] baseIDs,         int[] baseDamages,
        int[] targetPlayerIDs, int[] playerDamages,
        int[] buildingIDs,     int[] buildingDamages,
        int[] p1Pools,         int[] p2Pools,
        int[] predictedSourceIDs, int[] predictedEffectIndexes, int[] predictedSeeds, int[] predictedDeferKeys,
        int[] predictedEventSubjectIDs, int[] predictedTargetIDs,
        int[] tokenSpawnSourceIDs, int[] tokenSpawnEffectIdxs, int[] tokenSpawnPlayerIdxs,
        int[] tokenSpawnCardIDs,   int[] tokenSpawnCreatureIDs, int[] tokenSpawnTablePos,
        int[] tokenSpawnBaseIDs,   int[] tokenSpawnDeferKeys,
        int[] battleStartZoneKeys, int[] battleStartSourceIDs, int[] battleStartIsBuilding,
        int[] battleStartEffectIndexes, int[] battleStartSeeds)
    {
        ZoneCombatResolver.ApplyCanonicalAssignment(
            creatureIDs, creatureDamages, baseIDs, baseDamages,
            targetPlayerIDs, playerDamages, buildingIDs, buildingDamages);
        ZoneCombatResolver.ApplyCanonicalPools(p1Pools, p2Pools);

        // OnBattleStart précède logiquement les dégâts/morts de combat : rejoué en premier.
        if (!IsServer)
            for (int i = 0; i < battleStartSourceIDs.Length; i++)
            {
                // Debug.Log($"[DBG][ApplyCanonical] OnBattleStart replay #{i} — zoneKey={battleStartZoneKeys[i]} sourceID={battleStartSourceIDs[i]} isBuilding={battleStartIsBuilding[i]} effectIdx={battleStartEffectIndexes[i]}");
                ZoneCombatResolver.ReplayOnBattleStartEffect(
                    battleStartZoneKeys[i], battleStartSourceIDs[i], battleStartIsBuilding[i] != 0,
                    battleStartEffectIndexes[i], battleStartSeeds[i]);
            }

        // Le serveur a déjà résolu ces effets (OnDeath et OnAttack, entrelacés dans l'ordre
        // chronologique réel — voir ZoneCombatResolver.PredictedTriggerReplay) réellement pendant sa
        // propre planification (CreatureLogic.ResolvePredictedBattleDeath / ResolvePredictedOnAttack)
        // — seuls les autres clients rejouent, avec la même seed, pour obtenir exactement le même
        // ciblage/résultat.
        if (!IsServer)
        {
            // Consommé une fois appliqué : empêche qu'un token déjà rejoué pour une occurrence
            // antérieure du même couple (sourceID, effectIdx) — ex: une créature touchée deux fois
            // dans la même bataille par un trigger OnTakeDamage qui spawn un token — soit réappliqué
            // à une occurrence ultérieure de ce même couple (voir bug: ArgumentException clé dupliquée
            // dans CardsCreatedThisGame, le token étant recréé deux fois avec le même networkID).
            bool[] tokenSpawnConsumed = new bool[tokenSpawnSourceIDs.Length];
            for (int i = 0; i < predictedSourceIDs.Length; i++)
            {
                // Debug.Log($"[DBG][ApplyCanonical] Predicted replay #{i} — sourceID={predictedSourceIDs[i]} effectIdx={predictedEffectIndexes[i]}");
                CreatureLogic.ReplayPredictedTriggerEffect(predictedSourceIDs[i], predictedEffectIndexes[i], predictedSeeds[i], predictedDeferKeys[i], predictedEventSubjectIDs[i], predictedTargetIDs[i]);

                // Tokens créés par CETTE entrée précise (même paire source/effet — voir
                // ZoneCombatResolver.PredictedTokenSpawn) : rejoués tout de suite après, jamais avant —
                // pour qu'un trigger prédit ultérieur dans cette même boucle (ex: un autre OnAttack à
                // ciblage aléatoire) voie exactement le même état de zone qu'a vu le serveur au même
                // point chronologique de la planification.
                for (int j = 0; j < tokenSpawnSourceIDs.Length; j++)
                {
                    if (tokenSpawnConsumed[j]) continue;
                    if (tokenSpawnSourceIDs[j] != predictedSourceIDs[i] || tokenSpawnEffectIdxs[j] != predictedEffectIndexes[i]) continue;
                    tokenSpawnConsumed[j] = true;
                    ApplyTokenSpawnOnClient(
                        tokenSpawnPlayerIdxs[j], tokenSpawnSourceIDs[j], tokenSpawnEffectIdxs[j],
                        tokenSpawnTablePos[j], tokenSpawnBaseIDs[j], tokenSpawnCardIDs[j],
                        tokenSpawnCreatureIDs[j], tokenSpawnDeferKeys[j]);
                }
            }
        }
    }

    static int[] ConcatArrays(int[] firstArray, int[] secondArray)
    {
        int[] result = new int[firstArray.Length + secondArray.Length];
        firstArray.CopyTo(result, 0);
        secondArray.CopyTo(result, firstArray.Length);
        return result;
    }

    // -------------------------------------------------------------------------
    // SYNCHRONISATION BEGINCOMBAT — EFFETS CIBLÉS
    // -------------------------------------------------------------------------

    private struct EffectTargetSubmission
    {
        public int[] SourceEntityIDs;
        public int[] EffectIndexes;
        public int[] SelectedTargetIDs;
    }
    private Dictionary<int, EffectTargetSubmission> _effectSubmissions = new Dictionary<int, EffectTargetSubmission>();

    /// <summary>
    /// Envoyé par chaque joueur quand il confirme ses sélections de targets (toutes phases).
    /// Le serveur attend les deux soumissions, merge, puis broadcast la résolution canonique.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SubmitEffectTargetsServerRpc(
        int playerIndex,
        TurnManager.TurnPhases forPhase,
        int[] sourceEntityIDs,
        int[] effectIndexes,
        int[] selectedTargetIDs,
        RpcParams rpcParams = default)
    {
        bool isIndependent = forPhase == TurnManager.TurnPhases.Regroup
                          || forPhase == TurnManager.TurnPhases.Command;

        if (isIndependent)
        {
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            // Met à jour l'état de jeu (sans visuel, sans compléter le pipeline local — voir
            // PhaseEffectPipeline.ApplyCanonicalResolution isLocalPlayer:false) chez TOUS les
            // clients autres que l'expéditeur — symétrique, que l'expéditeur soit le host ou un
            // client distant. Un ClientRpc appelé côté serveur s'exécute aussi localement quand
            // il cible le serveur/host, donc ceci couvre également le cas host-non-sender sans
            // appel direct séparé.
            List<ulong> otherClientIds = new List<ulong>();
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                if (clientId != senderClientId)
                    otherClientIds.Add(clientId);

            if (otherClientIds.Count > 0)
            {
                ClientRpcParams otherParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = otherClientIds.ToArray() }
                };
                ApplyOpponentEffectResolutionClientRpc(sourceEntityIDs, effectIndexes, selectedTargetIDs, seed, otherParams);
            }

            // Feedback visuel + complétion du pipeline local, ciblés au seul joueur qui soumet.
            ClientRpcParams senderParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { senderClientId }
                }
            };
            Debug.Log($"[GameNetworkManager] SubmitEffectTargets indépendant — joueur {playerIndex}, phase {forPhase}");
            ApplyCanonicalEffectResolutionClientRpc(sourceEntityIDs, effectIndexes, selectedTargetIDs, seed, senderParams);
        }
        else
        {
            _effectSubmissions[playerIndex] = new EffectTargetSubmission
            {
                SourceEntityIDs   = sourceEntityIDs,
                EffectIndexes     = effectIndexes,
                SelectedTargetIDs = selectedTargetIDs
            };

            if (_effectSubmissions.Count < Player.Players.Length)
                return; // On attend encore l'autre joueur

            EffectTargetSubmission submission0 = _effectSubmissions[0];
            EffectTargetSubmission submission1 = _effectSubmissions[1];
            _effectSubmissions.Clear();

            int effectSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            Debug.Log($"[GameNetworkManager] SubmitEffectTargets synchronisé — phase {forPhase}, les deux joueurs prêts");
            ApplyCanonicalEffectResolutionClientRpc(
                ConcatArrays(submission0.SourceEntityIDs,   submission1.SourceEntityIDs),
                ConcatArrays(submission0.EffectIndexes,     submission1.EffectIndexes),
                ConcatArrays(submission0.SelectedTargetIDs, submission1.SelectedTargetIDs),
                effectSeed);
        }
    }

    /// <summary>
    /// Reçu par les clients ciblés : exécute les effets avec les targets choisies.
    /// En mode indépendant (Regroup/Command) : ciblé au joueur soumettant uniquement.
    /// En mode synchronisé : broadcast à tous les clients.
    /// </summary>
    [ClientRpc]
    void ApplyCanonicalEffectResolutionClientRpc(
        int[] sourceEntityIDs,
        int[] effectIndexes,
        int[] selectedTargetIDs,
        int effectSeed,
        ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[ClientRpc] ApplyCanonicalEffectResolution reçu — {sourceEntityIDs.Length} effet(s), seed={effectSeed}, phase={TurnManager.Instance.CurrentPhase}");
        PhaseEffectPipeline.ApplyCanonicalResolution(
            sourceEntityIDs, effectIndexes, selectedTargetIDs, effectSeed
        );
    }

    /// <summary>
    /// Reçu par les clients AUTRES que l'expéditeur d'un effet indépendant (Regroup/Command) :
    /// applique uniquement l'état de jeu (pas de visuel, pas de complétion du pipeline local —
    /// voir PhaseEffectPipeline.ApplyCanonicalResolution isLocalPlayer:false).
    /// </summary>
    [ClientRpc]
    void ApplyOpponentEffectResolutionClientRpc(
        int[] sourceEntityIDs,
        int[] effectIndexes,
        int[] selectedTargetIDs,
        int effectSeed,
        ClientRpcParams clientRpcParams = default)
    {
        PhaseEffectPipeline.ApplyCanonicalResolution(
            sourceEntityIDs, effectIndexes, selectedTargetIDs, effectSeed, isLocalPlayer: false
        );
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
        List<int> creatureIDList        = new List<int>();
        List<int> creatureHealthList    = new List<int>();
        List<int> creatureMaxHealthList = new List<int>();
        List<int> creatureAttackList    = new List<int>();
        List<int> creatureBaseIDList    = new List<int>();
        List<int> attacksLeftList       = new List<int>();
        List<int> movementsLeftList     = new List<int>();

        foreach (KeyValuePair<int, CreatureLogic> entry in CreatureLogic.CreaturesCreatedThisGame)
        {
            creatureIDList.Add(entry.Key);
            creatureHealthList.Add(entry.Value.Health);
            creatureMaxHealthList.Add(entry.Value.MaxHealth);
            creatureAttackList.Add(entry.Value.Attack);
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

        for (int i = 0; i < playerCount; i++)
        {
            playerHealths[i]   = Player.Players[i].Health;
            playerMainRes[i]   = Player.Players[i].mainRessourceAvailable;
        }

        SyncFullGameStateClientRpc(
            creatureIDList.ToArray(), creatureHealthList.ToArray(), creatureMaxHealthList.ToArray(),
            creatureAttackList.ToArray(), creatureBaseIDList.ToArray(),
            attacksLeftList.ToArray(), movementsLeftList.ToArray(),
            baseIDList.ToArray(), baseHealthList.ToArray(),
            playerHealths, playerMainRes);
    }

    /// <summary>
    /// Reçu par les clients (pas le serveur) : corrige l'état local pour qu'il corresponde
    /// à l'état autoritaire du serveur. Logge toute correction détectée pour faciliter
    /// le débogage des désynchronisations résiduelles.
    /// Ne déclenche PAS d'événements de mort — sert uniquement à corriger des valeurs.
    /// </summary>
    [ClientRpc]
    void SyncFullGameStateClientRpc(
        int[] creatureIDs,   int[] creatureHealths, int[] creatureMaxHealths,
        int[] creatureAttacks, int[] creatureBaseIDs,
        int[] attacksLeft,   int[] movementsLeft,
        int[] baseIDs,       int[] baseHealths,
        int[] playerHealths, int[] playerMainRes)
    {
        if (IsServer) return; // Le serveur est la source de vérité

        var serverCreatureIDSet = new System.Collections.Generic.HashSet<int>(creatureIDs);

        for (int i = 0; i < creatureIDs.Length; i++)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureIDs[i], out CreatureLogic creature))
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} : présente côté serveur (HP={creatureHealths[i]}/{creatureMaxHealths[i]}, ATK={creatureAttacks[i]}) mais absente côté client.");
                continue;
            }

            bool statsChanged = false;

            if (creature.Health != creatureHealths[i] && creatureHealths[i] > 0)
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} ({creature.DisplayName}) : HP local={creature.Health}, serveur={creatureHealths[i]}. Correction appliquée.");
                creature.Health = creatureHealths[i];
                statsChanged = true;
            }
            if (creature.MaxHealth != creatureMaxHealths[i])
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} ({creature.DisplayName}) : MaxHP local={creature.MaxHealth}, serveur={creatureMaxHealths[i]}. Correction appliquée.");
                creature.MaxHealth = creatureMaxHealths[i];
                statsChanged = true;
            }
            if (creature.Attack != creatureAttacks[i])
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} ({creature.DisplayName}) : ATK locale={creature.Attack}, serveur={creatureAttacks[i]}. Correction appliquée.");
                creature.Attack = creatureAttacks[i];
                statsChanged = true;
            }

            if (statsChanged)
            {
                GameObject creatureGO = IDHolder.GetGameObjectWithID(creatureIDs[i]);
                OneCreatureManager manager = creatureGO != null ? creatureGO.GetComponent<OneCreatureManager>() : null;
                if (manager != null)
                {
                    manager.AttackText.text = creature.Attack.ToString();
                    manager.HealthText.text = creature.Health.ToString();
                }
            }
            if (creature.AttacksLeftThisTurn != attacksLeft[i])
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} ({creature.DisplayName}) : AttacksLeft local={creature.AttacksLeftThisTurn}, serveur={attacksLeft[i]}. Correction appliquée.");
                creature.AttacksLeftThisTurn = attacksLeft[i];
            }
            if (creature.MovementsLeftThisTurn != movementsLeft[i])
            {
                Debug.LogError($"[Desync] Créature {creatureIDs[i]} ({creature.DisplayName}) : MovementsLeft local={creature.MovementsLeftThisTurn}, serveur={movementsLeft[i]}. Correction appliquée.");
                creature.MovementsLeftThisTurn = movementsLeft[i];
            }
        }

        foreach (int localID in CreatureLogic.CreaturesCreatedThisGame.Keys)
        {
            if (!serverCreatureIDSet.Contains(localID))
            {
                CreatureLogic.CreaturesCreatedThisGame.TryGetValue(localID, out CreatureLogic c);
                Debug.LogError($"[Desync] Créature {localID} ({c?.DisplayName ?? "??"}) : présente côté client (HP={c?.Health}/{c?.MaxHealth}, ATK={c?.Attack}) mais absente de l'état serveur.");
            }
        }

        for (int i = 0; i < baseIDs.Length; i++)
        {
            if (!BaseLogic.BasesCreatedThisGame.TryGetValue(baseIDs[i], out BaseLogic _base))
            {
                Debug.LogError($"[Desync] Base {baseIDs[i]} : présente côté serveur (HP={baseHealths[i]}) mais absente côté client.");
                continue;
            }

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

        ResolveCrossingRedirects();

        // Sort: player 0's actions come before player 1's, preserving relative order within each player
        List<PendingAction> p0Actions = _actionBuffer.FindAll(a => a.playerIndex == 0);
        List<PendingAction> p1Actions = _actionBuffer.FindAll(a => a.playerIndex == 1);

        foreach (PendingAction action in p0Actions) ExecuteAction(action);
        foreach (PendingAction action in p1Actions) ExecuteAction(action);

        _actionBuffer.Clear();
    }

    /// <summary>
    /// Serveur uniquement. Détecte les croisements d'armées parmi les MoveCreature bufferisés
    /// et redirige leur targetBaseID (param2) vers un CrossingZoneSlot avant exécution — les
    /// clients reçoivent directement le baseID déjà corrigé via MoveCreatureClientRpc.
    /// </summary>
    private void ResolveCrossingRedirects()
    {
        List<CommandMoveTracker.PendingMoveInfo> pending = new();
        foreach (PendingAction action in _actionBuffer)
        {
            if (action.type != ActionType.MoveCreature) continue;
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(action.param1, out CreatureLogic creature)) continue;
            pending.Add(new CommandMoveTracker.PendingMoveInfo
            {
                creatureID = action.param1,
                originBaseID = creature.BaseID,
                targetBaseID = action.param2,
                tablePos = action.param3,
                owner = creature.owner
            });
        }

        CommandMoveTracker.CrossingRedirectResult crossingResult = CommandMoveTracker.ResolveCrossings(pending);
        if (crossingResult.Redirects.Count == 0) return;

        for (int i = 0; i < _actionBuffer.Count; i++)
        {
            PendingAction a = _actionBuffer[i];
            if (a.type == ActionType.MoveCreature && crossingResult.Redirects.TryGetValue(a.param1, out int redirectedBaseID))
            {
                Debug.Log($"[Crossing][Server] MoveCreature redirigé — créature={a.param1} targetBaseID {a.param2}→{redirectedBaseID} tablePos={a.param3}");
                a.param2 = redirectedBaseID;
                _actionBuffer[i] = a;
            }
        }

        // Le serveur a déjà appliqué ces ordres localement (CommandMoveTracker.ResolveCrossings) —
        // on les diffuse ici pour que les clients trient leur TableVisual de la même façon.
        BroadcastOrderUpdates(crossingResult.OrderUpdates);
    }

    private void BroadcastOrderUpdates(List<CommandMoveTracker.CrossingOrderUpdate> orderUpdates)
    {
        foreach (CommandMoveTracker.CrossingOrderUpdate update in orderUpdates)
        {
            int playerIndex = System.Array.IndexOf(Player.Players, update.owner);
            if (playerIndex < 0)
            {
                Debug.LogError($"[Crossing][Server] BroadcastOrderUpdates: owner {update.owner?.name} introuvable dans Player.Players — ordre NON diffusé.");
                continue;
            }
            Debug.Log($"[Crossing][Server] Broadcast ordre — playerIndex={playerIndex} baseID={update.baseID} meleeIDs=[{string.Join(",", update.meleeIDs)}] rangedIDs=[{string.Join(",", update.rangedIDs)}]");
            ReorderCreaturesClientRpc(playerIndex, update.baseID, update.meleeIDs, update.rangedIDs);
        }
    }

    /// <summary>
    /// Serveur uniquement. Le serveur a déjà appliqué le dispatch (relocalisation + ordre)
    /// localement (voir TurnManager.AutoAdvanceFromEndBattle) — ceci le réplique aux clients.
    /// </summary>
    public void BroadcastCrossingDispatch(CommandMoveTracker.CrossingDispatchResult dispatch)
    {
        if (!IsServer) return;
        BroadcastOrderUpdates(dispatch.OrderUpdates);

        int n = dispatch.Relocations.Count;
        int[] creatureIDs = new int[n];
        int[] baseIDs = new int[n];
        for (int i = 0; i < n; i++)
        {
            creatureIDs[i] = dispatch.Relocations[i].creatureID;
            baseIDs[i] = dispatch.Relocations[i].baseID;
        }
        BroadcastCrossingDispatchClientRpc(creatureIDs, baseIDs);
    }

    [ClientRpc]
    void BroadcastCrossingDispatchClientRpc(int[] creatureIDs, int[] baseIDs)
    {
        if (IsServer) return; // le serveur a déjà appliqué directement.
        Debug.Log($"[Crossing][Client] Dispatch reçu — créatures=[{string.Join(",", creatureIDs)}] baseIDs=[{string.Join(",", baseIDs)}]");
        for (int i = 0; i < creatureIDs.Length; i++)
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureIDs[i], out CreatureLogic creature))
                creature.RelocateAfterCombat(baseIDs[i], 0);
            else
                Debug.LogError($"[Crossing][Client] Dispatch: créature introuvable id={creatureIDs[i]}");
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
            case ActionType.PlaceBuilding:
                PlaceBuildingClientRpc(action.playerIndex, action.param1, action.param2, action.param3);
                break;
            case ActionType.PlaySpell:
                PlaySpellClientRpc(action.playerIndex, action.param1);
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
        if (IsServer)
            mapIndex.Value = NetworkSessionData.SelectedMapIndex;

        LoadMap(mapIndex.Value);

        NetworkSessionData.LocalClientId = NetworkManager.Singleton.LocalClientId;
        PlayerReadyServerRpc(NetworkManager.Singleton.LocalClientId, NetworkSessionData.SelectedDeckPresetIndex);
    }

    public DeckSO GetDeckPresetForPlayer(int idx)
    {
        if (idx < 0 || registry == null || idx >= registry.decks.Length) 
        { 
            return null; 
        }
        return registry.decks[idx];
    }

    void LoadMap(int index)
    {
        Transform env = MapLoader.EnvironnementTransform;
        if (env == null) 
        { 
            Debug.LogError("EnvironnementTransform est null"); 
            return; 
        }
        Instantiate(MapLoader.Instance.GetMapPrefab(index), env.position, env.rotation, env);
        if (GlobalSettings.Instance != null) 
            GlobalSettings.Instance.InitFromMap();
        if (FogMapOverlay.Instance != null) 
            FogMapOverlay.Instance.ComputeMapBounds();
    }

    /// <summary>
    /// Envoyé par chaque client au serveur pour signaler qu'il est prêt.
    /// RequireOwnership = false : n'importe quel client peut appeler ce ServerRpc.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void PlayerReadyServerRpc(ulong clientId, int deckIndex)
    {
        _deckChoices[clientId] = deckIndex;
        readyCount++;
        Debug.Log($"[GameNetworkManager] Joueur prêt : {readyCount}/2");

        if (readyCount >= 2)
        {
            int deckLow = _deckChoices.TryGetValue(0, out int dLow) ? dLow : -1;
            int deckTop = -1;
            foreach (KeyValuePair<ulong, int> kvp in _deckChoices)
            {
                if (kvp.Key != 0)
                {
                    deckTop = kvp.Value;
                    break;
                }
            }
            deckSeed.Value = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            int[] heroCardIDs = new int[Player.Players.Length];
            for (int i = 0; i < heroCardIDs.Length; i++)
            {
                heroCardIDs[i] = IDFactory.GetUniqueID();
            }

            int[] cardInHandIDs = new int[TurnManager.Instance.initdraw * Player.Players.Length];
            for (int i = 0; i < cardInHandIDs.Length; i++)
            {
                cardInHandIDs[i] = IDFactory.GetUniqueID();
            }
            Debug.Log("[GameNetworkManager] Les deux joueurs sont prêts. Démarrage de la partie.");
            StartGameClientRpc(deckSeed.Value, cardInHandIDs, deckLow, deckTop, heroCardIDs);
        }
    }

    /// <summary>
    /// Envoyé par le serveur à TOUS les clients pour démarrer la partie.
    /// </summary>
    [ClientRpc]
    void StartGameClientRpc(int deckSeed, int[] cardInHandIDs, int deckIdxLow = -1, int deckIdxTop = -1, int[] heroCardIDs = null)
    {
        // 1. Assigner le local player
        AssignLocalPlayerControl();

        // 2. Lancer la logique de démarrage (distribution des cartes, ressources, etc.)
        TurnManager.Instance.OnGameStart(deckSeed, cardInHandIDs, deckIdxLow, deckIdxTop, heroCardIDs);

        // 3. Rafraîchir les boutons maintenant que AllowedToControlThisPlayer est correct
        GlobalSettings.Instance.RefreshEndPhaseButtons();
        ZoneEnemyIndicator.RefreshAll();
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
        gs.SyncLocalPlayerZones();

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
    public void PlayCreatureServerRpc(
        int cardUniqueID, int tablePos, int baseID, int playerIndex,
        int[] onPlayEffectIndexes, int[] onPlaySelectedTargetIDs)
    {
        int creatureUniqueID = IDFactory.GetUniqueID();

        bool isCelerity = CardLogic.CardsCreatedThisGame.TryGetValue(cardUniqueID, out CardLogic card) && card.ca.Celerity;

        // Seed généré côté serveur et rejoué identiquement sur tous les clients (voir
        // Player.NetworkPendingPlayCreature / NetworkPlayCreatureFromHand) : garantit que tout tirage
        // aléatoire dans la résolution de l'effet OnPlay (ex: EffectRepartition.RandomSingleTarget)
        // retombe sur la même cible partout, comme pour PlaySpellServerRpc.
        int seed = Random.Range(int.MinValue, int.MaxValue);

        if (isCelerity)
        {
            ImmediatePlayCreatureClientRpc(playerIndex, cardUniqueID, creatureUniqueID, tablePos, baseID,
                onPlayEffectIndexes, onPlaySelectedTargetIDs, seed);
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
            ShowPendingPlayCreatureClientRpc(playerIndex, cardUniqueID, creatureUniqueID, tablePos, baseID,
                onPlayEffectIndexes, onPlaySelectedTargetIDs, seed);
        }
    }

    /// <summary>
    /// Reçu par TOUS les clients : exécute la logique + le visuel de jouer une créature
    /// avec les mêmes identifiants sur toutes les machines.
    /// </summary>
    [ClientRpc]
    void ShowPendingPlayCreatureClientRpc(
        int playerIndex, int cardUniqueID, int creatureUniqueID, int tablePos, int baseID,
        int[] onPlayEffectIndexes, int[] onPlaySelectedTargetIDs, int seed)
    {
        Player player = Player.Players[playerIndex];
        player.NetworkPendingPlayCreature(cardUniqueID, creatureUniqueID, tablePos, baseID,
            onPlayEffectIndexes, onPlaySelectedTargetIDs, seed);
    }

    [ClientRpc]
    void ImmediatePlayCreatureClientRpc(
        int playerIndex, int cardUniqueID, int creatureUniqueID, int tablePos, int baseID,
        int[] onPlayEffectIndexes, int[] onPlaySelectedTargetIDs, int seed)
    {
        Player player = Player.Players[playerIndex];
        player.NetworkPlayCreatureFromHand(cardUniqueID, creatureUniqueID, tablePos, baseID,
            onPlayEffectIndexes, onPlaySelectedTargetIDs, seed);
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
    // ACTIONS DE JEU — JOUER UN SORT
    // -------------------------------------------------------------------------

    /// <summary>
    /// Envoyé par un client pour jouer un sort depuis sa main. Contrairement à une créature, un
    /// sort ne place rien sur une table : pas d'ID à faire générer par le serveur, seuls
    /// cardUniqueID et les tableaux de ciblage (déjà résolus localement via OnPlayTargetingSession,
    /// vides pour un sort sans cible) sont nécessaires.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlaySpellServerRpc(int playerIndex, int cardUniqueID, int[] effectIndexes, int[] selectedTargetIDs)
    {
        RegisterAction(new PendingAction
        {
            type = ActionType.PlaySpell,
            playerIndex = playerIndex,
            param1 = cardUniqueID
        });
        // Seed généré côté serveur et rejoué identiquement sur tous les clients (voir
        // Player.NetworkPendingPlaySpell) : garantit que tout tirage aléatoire dans la résolution de
        // l'effet (ex: EffectRepartition.RandomSingleTarget) retombe sur la même cible partout,
        // comme pour les triggers de combat (EffectRegistry.FireListenersPredicted).
        int seed = Random.Range(int.MinValue, int.MaxValue);
        ShowPendingPlaySpellClientRpc(playerIndex, cardUniqueID, effectIndexes, selectedTargetIDs, seed);
    }

    /// <summary>
    /// Reçu par TOUS les clients : applique l'état de jeu (ressources, main, effets) avec les
    /// mêmes cibles sur toutes les machines. Pas de visuel ici — voir PlaySpellClientRpc.
    /// </summary>
    [ClientRpc]
    void ShowPendingPlaySpellClientRpc(int playerIndex, int cardUniqueID, int[] effectIndexes, int[] selectedTargetIDs, int seed)
    {
        Player player = Player.Players[playerIndex];
        player.NetworkPendingPlaySpell(cardUniqueID, effectIndexes, selectedTargetIDs, seed);
    }

    [ClientRpc]
    void PlaySpellClientRpc(int playerIndex, int cardUniqueID)
    {
        if (Player.Players == null || playerIndex < 0 || playerIndex >= Player.Players.Length)
        {
            Debug.LogError($"[GameNetworkManager] PlaySpellClientRpc : playerIndex {playerIndex} invalide");
            return;
        }
        Player player = Player.Players[playerIndex];
        player.NetworkFlushPlaySpell(cardUniqueID);
    }

    // -------------------------------------------------------------------------
    // ZONE DE RÉSERVE (CardHoldSlotVisual)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Envoyé par le joueur qui vient de déposer une carte dans son slot de réserve. Contrairement
    /// aux actions de jeu (créature/sort), rien ici n'a besoin d'être généré par le serveur : la
    /// carte est déjà appliquée localement chez l'expéditeur (voir CardHoldSlotVisual.TryHoldCard).
    /// On relaie donc juste l'info aux AUTRES machines, comme pour un effet indépendant de phase
    /// (voir SubmitEffectTargetsServerRpc) — sans ça ReservedCard resterait local à l'expéditeur et
    /// DiscardHand, qui tourne indépendamment sur chaque client en fin de phase Command, finirait
    /// par désynchroniser les mains (une machine garde la carte, l'autre la discard).
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SyncHoldCardServerRpc(int playerIndex, int cardUniqueID, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        List<ulong> otherClientIds = new List<ulong>();
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            if (clientId != senderClientId)
                otherClientIds.Add(clientId);

        if (otherClientIds.Count == 0) return;

        ClientRpcParams otherParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = otherClientIds.ToArray() }
        };
        SyncHoldCardClientRpc(playerIndex, cardUniqueID, otherParams);
    }

    /// <summary>
    /// Reçu par les machines autres que l'expéditeur : rejoue le dépôt de carte sur le
    /// CardHoldSlotVisual de ce joueur. ReservedCard (la donnée qui compte pour DiscardHand) est
    /// fixée tout de suite via CardLogic.CardsCreatedThisGame, déjà peuplé de façon synchrone dès le
    /// tirage (voir Player.DrawACard / DrawAcardClientRpc) — indépendamment du GameObject visuel de
    /// la carte, qui lui n'apparaît qu'une fois son DrawACardCommand animé passé en tête de la
    /// Command Queue LOCALE à cette machine (pas synchronisée en temps réel avec celle de
    /// l'expéditeur : cette machine peut donc être encore en retard sur l'animation de tirage au
    /// moment où cette RPC arrive). Le placement visuel dans le slot est donc simplement différé
    /// jusqu'à ce que le GameObject existe, plutôt que d'échouer.
    /// </summary>
    [ClientRpc]
    void SyncHoldCardClientRpc(int playerIndex, int cardUniqueID, ClientRpcParams clientRpcParams = default)
    {
        if (Player.Players == null || playerIndex < 0 || playerIndex >= Player.Players.Length)
        {
            Debug.LogError($"[GameNetworkManager] SyncHoldCardClientRpc : playerIndex {playerIndex} invalide");
            return;
        }
        if (!CardLogic.CardsCreatedThisGame.TryGetValue(cardUniqueID, out CardLogic card))
        {
            Debug.LogError($"[GameNetworkManager] SyncHoldCardClientRpc : CardLogic introuvable cardUniqueID={cardUniqueID}");
            return;
        }

        Player player = Player.Players[playerIndex];
        player.ReservedCard = card;
        StartCoroutine(ApplyHoldVisualWhenCardReady(player, cardUniqueID));
    }

    IEnumerator ApplyHoldVisualWhenCardReady(Player player, int cardUniqueID)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        GameObject cardGO = IDHolder.GetGameObjectWithID(cardUniqueID);
        while (cardGO == null && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            cardGO = IDHolder.GetGameObjectWithID(cardUniqueID);
        }
        if (cardGO == null)
        {
            Debug.LogError($"[GameNetworkManager] SyncHoldCardClientRpc : GameObject jamais apparu pour cardUniqueID={cardUniqueID}");
            yield break;
        }

        // ReservedCard a pu changer entre-temps (carte évincée/rejouée avant même d'avoir fini
        // d'apparaître) : ne pose visuellement que si cette carte est toujours la réservée actuelle.
        if (player.ReservedCard == null || player.ReservedCard.UniqueCardID != cardUniqueID)
            yield break;

        CardHoldSlotVisual.ForPlayer(player)?.ApplyHold(cardGO);
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
        if (TurnManager.Instance.CurrentPhase == forPhase)
        {
            Debug.Log($"[GameNetworkManager] RegisterEndPhase reçu — joueur {playerIndex}, phase {forPhase}");
            // ForceRegisterEndPhase bypasse le check AllowedToControlThisPlayer, qui n'a de sens
            // que côté client (pour décider d'envoyer le RPC), pas côté serveur (qui traite la requête).
            TurnManager.Instance.ForceRegisterEndPhase(playerIndex);
        }
        else
        {
            if (!_pendingEndPhase.ContainsKey(forPhase))
                _pendingEndPhase[forPhase] = new HashSet<int>();
            _pendingEndPhase[forPhase].Add(playerIndex);
            Debug.Log($"[GameNetworkManager] RegisterEndPhase stocké — joueur {playerIndex}, phase {forPhase} (actuelle: {TurnManager.Instance.CurrentPhase})");
        }
    }

    void FlushPendingEndPhase(TurnManager.TurnPhases phase)
    {
        if (!_pendingEndPhase.TryGetValue(phase, out var pending)) return;
        _pendingEndPhase.Remove(phase);
        foreach (int idx in pending)
        {
            Debug.Log($"[GameNetworkManager] Flush RegisterEndPhase — joueur {idx}, phase {phase}");
            TurnManager.Instance.ForceRegisterEndPhase(idx);
        }
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
    /// Pour la transition vers Battle, draine d'abord les morts pendantes issues de BeginCombat.
    /// </summary>
    [ClientRpc]
    void PhaseTransitionClientRpc(TurnManager.TurnPhases nextPhase, bool roundEnded, int newRound)
    {
        Debug.Log($"[GameNetworkManager] Transition vers {nextPhase} (round {newRound}, finRound={roundEnded})");

        if (roundEnded)
        {
            foreach (Player p in Player.Players)
                p.GetComponent<TurnMaker>().OnTurnEnd();
        }

        TurnManager.Instance.SetCurrentRound(newRound);

        if (nextPhase == TurnManager.TurnPhases.Battle)
        {
            if (IsServer)
                StartCoroutine(DrainBeginCombatAndTransition());
            else
                Debug.Log("[PhaseTransition][Client] Battle reçu — en attente de BroadcastDeathDrainClientRpc");
            return;
        }

        // Guard: si le client est déjà dans cette phase (auto-advance), on ne re-entre pas.
        if (TurnManager.Instance.CurrentPhase == nextPhase)
        {
            Debug.Log($"[GameNetworkManager] PhaseTransitionClientRpc ignorée — déjà en {nextPhase}");
            return;
        }

        TurnManager.Instance.EnterPhase(nextPhase);

        if (IsServer)
        {
            FlushPendingEndPhase(nextPhase);
            BroadcastFullGameState();
        }
    }

    // -------------------------------------------------------------------------
    // DRAIN DES MORTS — END → REGROUP (autoritaire serveur)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Appelé par le serveur après avoir enregistré le drain complet.
    /// Sérialise la séquence ordonnée d'événements et la broadcast à tous les clients.
    /// </summary>
    public void BroadcastDeathDrain(List<DeathDrainRecorder.DrainEvent> events, TurnManager.TurnPhases nextPhase = TurnManager.TurnPhases.Regroup)
    {
        if (!IsServer) return;
        int n = events.Count;
        int[] types        = new int[n];
        int[] creatureIDs  = new int[n];
        int[] sourceIDs    = new int[n];
        int[] targetIDs    = new int[n];
        int[] damages      = new int[n];
        int[] healthAfters = new int[n];
        for (int i = 0; i < n; i++)
        {
            types[i]        = (int)events[i].Type;
            creatureIDs[i]  = events[i].CreatureID;
            sourceIDs[i]    = events[i].SourceID;
            targetIDs[i]    = events[i].TargetID;
            damages[i]      = events[i].Damage;
            healthAfters[i] = events[i].HealthAfter;
        }
        BroadcastDeathDrainClientRpc(types, creatureIDs, sourceIDs, targetIDs, damages, healthAfters, (int)nextPhase);
    }

    /// <summary>
    /// Reçu par TOUS les clients (y compris le serveur/host).
    /// Côté client  : rejoue la séquence (SilentDie + animations) puis entre dans nextPhase.
    /// Côté serveur : les changements sont déjà appliqués — attend juste la fin de la queue.
    /// </summary>
    [ClientRpc]
    void BroadcastDeathDrainClientRpc(
        int[] types, int[] creatureIDs, int[] sourceIDs,
        int[] targetIDs, int[] damages, int[] healthAfters,
        int nextPhase)
    {
        StartCoroutine(ApplyDeathDrainAndTransition(
            types, creatureIDs, sourceIDs, targetIDs, damages, healthAfters,
            (TurnManager.TurnPhases)nextPhase));
    }

    IEnumerator ApplyDeathDrainAndTransition(
        int[] types, int[] creatureIDs, int[] sourceIDs,
        int[] targetIDs, int[] damages, int[] healthAfters,
        TurnManager.TurnPhases nextPhase)
    {
        string drainRole = IsServer ? "[Server]" : "[Client]";
        // Debug.Log($"[DeathDrain]{drainRole} REPLAY — {types.Length} événement(s) reçu(s)");
        if (!IsServer)
        {
            // Rejouer la séquence dans le même ordre que le serveur.
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == (int)DeathDrainRecorder.EventType.Death)
                {
                    if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureIDs[i], out CreatureLogic creature))
                        creature.SilentDie();
                }
                else // Anim
                {
                    new DealDamageCommand(targetIDs[i], damages[i], healthAfters[i], sourceIDs[i], null).AddToQueue();
                }
            }
            CreatureLogic.PendingDeathList.Clear();
        }

        // Debug.Log($"[DeathDrain]{drainRole} WaitWhile queue démarré (playingQueue={Command.playingQueue})");
        yield return new WaitWhile(() => Command.playingQueue);
        // Debug.Log($"[DeathDrain]{drainRole} WaitWhile queue résolu → EnterPhase({nextPhase})");

        TurnManager.Instance.EnterPhase(nextPhase);

        if (IsServer)
        {
            FlushPendingEndPhase(nextPhase);
            BroadcastFullGameState();
        }
    }

    // Drain serveur-autoritaire de la transition BeginCombat→Battle.
    // Même pattern que AutoAdvanceFromEnd : le serveur enregistre les événements
    // (morts + animations Acid Explosion / chaîne) et les diffuse via BroadcastDeathDrain.
    // Le client attend ce RPC et rejoue avec SilentDie, sans randomness indépendante.
    IEnumerator DrainBeginCombatAndTransition()
    {
        DeathDrainRecorder.Begin();
        while (CreatureLogic.PendingDeathList.Count > 0)
            CreatureLogic.ProcessPendingDeaths();
        List<DeathDrainRecorder.DrainEvent> events = DeathDrainRecorder.End();
        // Debug.Log($"[DeathDrain][Server] BeginCombat drain — {events.Count} événement(s) enregistré(s)");
        BroadcastDeathDrain(events, TurnManager.TurnPhases.Battle);
        yield break;
    }

    private int _drawSeedOffset = 0;

    public void InitDrawSeedOffset(int value) { _drawSeedOffset = value; }

    public void BroadCastDrawCard(int playerIndex, bool fast = false)
    {
        if (!IsServer) return;
        int cardID = IDFactory.GetUniqueID();
        int finalSeed = DeckSeed + _drawSeedOffset++;
        // Debug.Log($"[BroadCastDrawCard] joueur={playerIndex} cardID={cardID} finalSeed={finalSeed} fast={fast}");
        DrawAcardClientRpc(playerIndex, cardID, finalSeed, fast);
    }

    [ClientRpc]
    public void DrawAcardClientRpc(int playerIndex, int cardID, int finalSeed, bool fast)
    {
        // Debug.Log($"[DrawAcardClientRpc] joueur={playerIndex} cardID={cardID} finalSeed={finalSeed} fast={fast}");
        Player player = Player.Players[playerIndex];
        player.DrawACard(fast, cardID, finalSeed);
    }

    public void BroadcastHeroReturnToHand(int playerIndex)
    {
        if (!IsServer) return;
        int cardID = IDFactory.GetUniqueID();
        // Debug.Log($"[BroadcastHeroReturnToHand] joueur={playerIndex} cardID={cardID}");
        HeroReturnToHandClientRpc(playerIndex, cardID);
    }

    [ClientRpc]
    void HeroReturnToHandClientRpc(int playerIndex, int cardID)
    {
        Player player = Player.Players[playerIndex];
        CardAsset heroAsset = player.deck.playerDeck.heroCard;
        if (heroAsset == null) return;

        heroAsset.UnlockCondition?.ResetProgress(player);
        player.GetACardNotFromDeck(heroAsset, cardID);
    }

    public void BroadCastGainRessources(int playerIndex, int amount, bool isRecurring, int sourceID)
    {
        if (!IsServer) return;
        GainRessourcesClientRpc(playerIndex, amount, isRecurring, sourceID);
    }

    [ClientRpc]
    public void GainRessourcesClientRpc(int playerIndex, int amount, bool isRecurring, int sourceID)
    {
        Player player = Player.Players[playerIndex];
        if (isRecurring)
        {
            if (sourceID != -1)
                player.AddBonusIncomeFromSource(sourceID, amount);
            else
                player.AddBonusIncome(amount);
        }
        else
            player.GetBonusRessources(amount);
    }

    public void BroadCastShieldBonus(int playerIndex, int amount, int sourceID)
    {
        if (!IsServer) return;
        ShieldBonusClientRpc(playerIndex, amount, sourceID);
    }

    [ClientRpc]
    public void ShieldBonusClientRpc(int playerIndex, int amount, int sourceID)
    {
        Player player = Player.Players[playerIndex];
        player.AddBonusShieldFromSource(sourceID, amount);
    }

    public void BroadCastEffectAmplifier(int playerIndex, int sourceID, EffectAmplifier amplifier)
    {
        if (!IsServer) return;
        EffectAmplifierClientRpc(playerIndex, sourceID, (int)amplifier.AppliesTo,
            amplifier.DamageBonus, amplifier.HealBonus, amplifier.AttackBonus, amplifier.HealthBonus, amplifier.SpellsOnly);
    }

    [ClientRpc]
    public void EffectAmplifierClientRpc(int playerIndex, int sourceID, int appliesTo,
        int damageBonus, int healBonus, int attackBonus, int healthBonus, bool spellsOnly)
    {
        Player player = Player.Players[playerIndex];
        player.AddEffectAmplifier(sourceID, new EffectAmplifier
        {
            AppliesTo = (EffectCategory)appliesTo,
            DamageBonus = damageBonus,
            HealBonus = healBonus,
            AttackBonus = attackBonus,
            HealthBonus = healthBonus,
            SpellsOnly = spellsOnly,
        });
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
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureUniqueID, out CreatureLogic creature))
            creature.IsPendingMove = true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CancelMoveCreatureServerRpc(int creatureUniqueID, int playerIndex, RpcParams rpcParams = default)
    {
        int removed = _actionBuffer.RemoveAll(a =>
            a.type == ActionType.MoveCreature &&
            a.param1 == creatureUniqueID &&
            a.playerIndex == playerIndex);

        if (removed == 0)
            return;

        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureUniqueID, out CreatureLogic creature))
            creature.IsPendingMove = false;

        // Le client à l'origine de l'annulation a déjà effacé sa propre flèche en local,
        // de façon synchrone, avant même d'envoyer ce RPC (voir DragCreatureActions.OnStartDrag /
        // TryGroupMoveTo). Si on le notifiait aussi via ce ClientRpc, l'appel arriverait après un
        // aller-retour réseau — donc potentiellement APRÈS qu'un nouveau déplacement (multi-select
        // sur une unité déjà en attente) ait déjà réaffiché sa flèche — et l'effacerait à tort.
        // On ne relaie donc l'annulation qu'aux autres clients (qui n'affichent de toute façon
        // jamais la flèche d'attente d'un adversaire).
        ulong senderId = rpcParams.Receive.SenderClientId;
        List<ulong> otherClients = new List<ulong>();
        foreach (ulong id in NetworkManager.ConnectedClientsIds)
        {
            if (id != senderId)
                otherClients.Add(id);
        }

        if (otherClients.Count == 0)
            return;

        ClientRpcParams targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = otherClients.ToArray() }
        };
        CancelMoveCreatureClientRpc(creatureUniqueID, targetParams);
    }

    [ClientRpc]
    void CancelMoveCreatureClientRpc(int creatureUniqueID, ClientRpcParams clientRpcParams = default)
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
        OneCreatureManager ocm = IDHolder.GetGameObjectWithID(creatureUniqueID)?.GetComponent<OneCreatureManager>();
        ocm?.ClearPendingMoveArrow();
        ocm?.DestroyPendingMoveGhost(); // no-op côté adversaire, qui n'en a jamais eu (ghost local uniquement)

        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureUniqueID, out CreatureLogic creature))
        {
            Debug.LogError($"[GameNetworkManager] MoveCreature: créature introuvable id={creatureUniqueID}");
            return;
        }
        creature.Move(targetBaseID, tablePos);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReorderCreaturesServerRpc(int playerIndex, int baseID, int[] meleeIDs, int[] rangedIDs)
    {
        Debug.Log($"[ReorderRpc][Server] playerIndex={playerIndex} baseID={baseID} meleeIDs=[{string.Join(",", meleeIDs)}] rangedIDs=[{string.Join(",", rangedIDs)}]");
        // Update buffered tablePos so the flush sends creatures in the correct final order to remote clients.
        // Remote clients have no pending creatures, so the ClientRpc alone would sort an empty list — too early.
        for (int i = 0; i < _actionBuffer.Count; i++)
        {
            PendingAction a = _actionBuffer[i];
            if (a.type != ActionType.PlayCreature || a.playerIndex != playerIndex || a.param4 != baseID)
                continue;

            int creatureID = a.param2;
            int[] row = System.Array.IndexOf(meleeIDs, creatureID) >= 0 ? meleeIDs : rangedIDs;
            int rawPos = System.Array.IndexOf(row, creatureID);
            if (rawPos >= 0)
            {
                // meleeIDs/rangedIDs incluent maintenant aussi les IDs permanents des ghosts de
                // déplacement en attente du client qui reorder (voir TableVisual.PermanentIDOf) : ce
                // sont de vrais IDs présents dans CreaturesCreatedThisGame (une créature en déplacement
                // reste "vivante" tout du long, seul son BaseID change, et seulement à la résolution —
                // voir CreatureLogic.Move). On ne peut donc plus se fier à la seule présence dans ce
                // dictionnaire pour exclure les entrées non résidentes : il faut vérifier que la
                // créature réside VRAIMENT déjà dans cette rangée (BaseID == baseID) pour retrouver
                // l'index logique ghost-free, le seul qui garde le même sens une fois bufferisé
                // (voir TableVisual.ToNetworkTablePos).
                int logicalPos = 0;
                for (int j = 0; j < rawPos; j++)
                    if (IsResidentCreature(row[j], baseID))
                        logicalPos++;

                Debug.Log($"[ReorderRpc][Server] patch PlayCreature creatureID={creatureID} rawPos={rawPos} → param3={logicalPos}");
                a.param3 = logicalPos;
                _actionBuffer[i] = a;
            }
        }

        ReorderCreaturesClientRpc(playerIndex, baseID, meleeIDs, rangedIDs);
    }

    private static bool IsResidentCreature(int creatureID, int baseID) =>
        CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureID, out CreatureLogic cl) && cl.BaseID == baseID;

    [ClientRpc]
    void ReorderCreaturesClientRpc(int playerIndex, int baseID, int[] meleeIDs, int[] rangedIDs)
    {
        Debug.Log($"[ReorderRpc][Client] playerIndex={playerIndex} baseID={baseID} meleeIDs=[{string.Join(",", meleeIDs)}] rangedIDs=[{string.Join(",", rangedIDs)}]");
        Player.Players[playerIndex].NetworkApplyCreatureOrder(baseID, meleeIDs, rangedIDs);
    }

    // //Attacking Units
    // [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // public void AttackCreatureServerRpc(int attackerID, int targetCreatureID)
    // {
    //     AttackCreatureClientRpc(attackerID, targetCreatureID);
    // }

    // [ClientRpc]
    // void AttackCreatureClientRpc(int attackerID, int targetCreatureID)
    // {
    //     if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic attacker))
    //     {
    //         Debug.LogError($"[GameNetworkManager] AttackCreature: attaquant introuvable id={attackerID}");
    //         return;
    //     }
    //     attacker.AttackCreatureWithID(targetCreatureID);
    // }

    // [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // public void AttackBaseServerRpc(int attackerID, int targetBaseID)
    // {
    //     AttackBaseClientRpc(attackerID, targetBaseID);
    // }

    // [ClientRpc]
    // void AttackBaseClientRpc(int attackerID, int targetBaseID)
    // {
    //     if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic attacker))
    //     {
    //         Debug.LogError($"[GameNetworkManager] AttackBase: attaquant introuvable id={attackerID}");
    //         return;
    //     }
    //     attacker.AttackBaseWithID(targetBaseID);
    // }

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

    ///Base Upgrade
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UpgradeBaseServerRpc(int playerIndex)
    {
        UpgradeBaseClientRpc(playerIndex);
    }

    [ClientRpc]
    void UpgradeBaseClientRpc(int playerIndex)
    {
        if (Player.Players == null || playerIndex < 0 || playerIndex >= Player.Players.Length)
        {
            Debug.LogError($"[GameNetworkManager] UpgradeBaseClientRpc : playerIndex {playerIndex} invalide");
            return;
        }
        Player.Players[playerIndex].homeBaseLogic?.TryUpgrade();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlaceBuildingServerRpc(int playerIndex, int buildingIndex, int spotID)
    {
        int buildingUniqueID = IDFactory.GetUniqueID();
        RegisterAction(new PendingAction
        {
            type = ActionType.PlaceBuilding,
            playerIndex = playerIndex,
            param1 = buildingIndex,
            param2 = spotID,
            param3 = buildingUniqueID
        });
    }
    [ClientRpc]
    void PlaceBuildingClientRpc(int playerIndex, int buildingIndex, int spotID, int buildingUniqueID)
    {
        if (!BuildSpotVisual.Registry.TryGetValue(spotID, out BuildSpotVisual spot))
        {
            Debug.LogError($"PlaceBuildingClientRpc: spot not found id={spotID}");
            return;
        }

        Player player = Player.Players[playerIndex];
        CardAsset building = player.deck.FindBuildingByIndex(buildingIndex);
        if (building == null)
        {
            Debug.LogError($"PlaceBuildingClientRpc: building not found index={buildingIndex}");
            return;
        }

        bool alreadyPaid = (player == GlobalSettings.Instance.localPlayer);
        player.ExecutePlaceBuilding(building, spot, buildingUniqueID, alreadyPaid);
    }

    // -------------------------------------------------------------------------
    // Effets de cartes
    // -------------------------------------------------------------------------

    public void BroadCastTokenToHand(int playerIndex, int sourceEntityID, int effectIndex)
    {
        if (!IsServer) return;
        int cardID = IDFactory.GetUniqueID();
        TokenToHandClientRpc(playerIndex, sourceEntityID, effectIndex, cardID);
    }

    [ClientRpc]
    void TokenToHandClientRpc(int playerIndex, int sourceEntityID, int effectIndex, int cardID)
    {
        CardAsset tokenAsset = EffectRegistry.GetTokenAsset(sourceEntityID, effectIndex);
        if (tokenAsset == null) { Debug.LogError($"[Token] Asset introuvable src={sourceEntityID} idx={effectIndex}"); return; }
        EffectVisualData visualData = EffectRegistry.GetTokenVisualData(sourceEntityID, effectIndex);
        Player.Players[playerIndex].GetACardNotFromDeck(tokenAsset, cardID, visualData);
    }

    public void BroadCastPoolCardToHand(int playerIndex, int sourceEntityID, int effectIndex, int seed)
    {
        if (!IsServer) return;
        int cardID = IDFactory.GetUniqueID();
        PoolCardToHandClientRpc(playerIndex, sourceEntityID, effectIndex, seed, cardID);
    }

    [ClientRpc]
    void PoolCardToHandClientRpc(int playerIndex, int sourceEntityID, int effectIndex, int seed, int cardID)
    {
        GenerateCardsFromPoolSO so = EffectRegistry.GetGenerateCardsFromPoolSO(sourceEntityID, effectIndex);
        if (so == null || so.CardPool == null || so.CardPool.Count == 0) return;

        CardAsset picked = so.CardPool[new System.Random(seed).Next(0, so.CardPool.Count)];
        Player.Players[playerIndex].GetACardNotFromDeck(picked, cardID, so.EffectVisual);
    }

    public void BroadCastChooseOneOffer(int playerIndex, int sourceEntityID, int effectIndex, int seed)
    {
        if (!IsServer) return;
        ChooseOneOfferClientRpc(playerIndex, sourceEntityID, effectIndex, seed);
    }

    [ClientRpc]
    void ChooseOneOfferClientRpc(int playerIndex, int sourceEntityID, int effectIndex, int seed)
    {
        ChooseOneSO so = EffectRegistry.GetChooseOneSO(sourceEntityID, effectIndex);
        if (so == null || so.CardPool == null || so.CardPool.Count == 0) return;

        int offerCount = Mathf.Clamp(so.ChooseBetweenCount, 1, so.CardPool.Count);
        List<CardAsset> offer = ChooseOneSO.PickDistinct(so.CardPool, offerCount, new System.Random(seed));

        ChooseOneManager.Instance.BeginOffer(Player.Players[playerIndex], offer, sourceEntityID, effectIndex);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SubmitChooseOnePickServerRpc(int playerIndex, int sourceEntityID, int effectIndex, int chosenPoolIndex)
    {
        ChooseOneSO so = EffectRegistry.GetChooseOneSO(sourceEntityID, effectIndex);
        if (so == null || so.CardPool == null || chosenPoolIndex < 0 || chosenPoolIndex >= so.CardPool.Count)
            return;

        int cardID = IDFactory.GetUniqueID();
        ChooseOnePickedClientRpc(playerIndex, sourceEntityID, effectIndex, chosenPoolIndex, cardID);
    }

    [ClientRpc]
    void ChooseOnePickedClientRpc(int playerIndex, int sourceEntityID, int effectIndex, int chosenPoolIndex, int cardID)
    {
        ChooseOneSO so = EffectRegistry.GetChooseOneSO(sourceEntityID, effectIndex);
        if (so == null || so.CardPool == null || chosenPoolIndex < 0 || chosenPoolIndex >= so.CardPool.Count) return;

        CardAsset chosen = so.CardPool[chosenPoolIndex];
        Player.Players[playerIndex].GetACardNotFromDeck(chosen, cardID);
    }

    // cardID/creatureID sont déjà alloués par l'appelant (TokenGenerationSO.Execute), qui a créé
    // la créature en autorité serveur AVANT cet appel — voir TokenToZoneClientRpc ci-dessous.
    // deferKey reflète Command.CurrentDeferSourceID au moment de l'appel serveur (int.MinValue =
    // pas de report) : le visuel ne doit être différé que si le token provient d'un trigger résolu
    // par anticipation en combat (OnDeath → ID de créature, OnBattleStart → zoneDeferKey, etc.),
    // pas d'un ETB normal à la pose. La clé elle-même dépend du trigger d'origine, pas d'une seule
    // convention fixe — voir Command.CurrentDeferSourceID.
    public void BroadCastTokenToZone(int playerIndex, int sourceEntityID, int effectIndex, int tablePos, int baseID, int cardID, int creatureID, int deferKey)
    {
        if (!IsServer) return;
        TokenToZoneClientRpc(playerIndex, sourceEntityID, effectIndex, tablePos, baseID, cardID, creatureID, deferKey);
    }

    // Le serveur a déjà créé cette créature localement, en autorité, dans TokenGenerationSO.SpawnToZone
    // (pour qu'elle puisse rejoindre la file d'attaque de la bataille en cours de planification —
    // voir ZoneCombatResolver.NotifyCreatureAddedDuringPlanning). L'hôte reçoit aussi ce ClientRpc
    // (il est son propre client) mais ne doit PAS recréer la créature ; seuls les autres clients le font.
    [ClientRpc]
    void TokenToZoneClientRpc(int playerIndex, int sourceEntityID, int effectIndex, int tablePos, int baseID, int cardID, int creatureID, int deferKey)
    {
        if (IsServer) return;
        ApplyTokenSpawnOnClient(playerIndex, sourceEntityID, effectIndex, tablePos, baseID, cardID, creatureID, deferKey);
    }

    // Résout l'asset/visuel du token puis crée la créature côté client — factorisé entre
    // TokenToZoneClientRpc (tokens hors combat prédit) et le rejeu inline de
    // ZoneCombatResolver.PredictedTokenSpawn dans ApplyCanonicalBattleAssignmentClientRpc (tokens
    // créés par un OnDeath/OnAttack résolu par anticipation — voir TokenGenerationSO.Execute).
    void ApplyTokenSpawnOnClient(int playerIndex, int sourceEntityID, int effectIndex, int tablePos, int baseID, int cardID, int creatureID, int deferKey)
    {
        CardAsset tokenAsset = EffectRegistry.GetTokenAsset(sourceEntityID, effectIndex);
        if (tokenAsset == null) { Debug.LogError($"[Token] Asset introuvable src={sourceEntityID} idx={effectIndex}"); return; }
        EffectVisualData visualData = EffectRegistry.GetTokenVisualData(sourceEntityID, effectIndex);
        Player.Players[playerIndex].NetworkSpawnTokenToZone(tokenAsset, cardID, creatureID, tablePos, baseID, deferKey, visualData);
    }


    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void NotifyOpponentSelectingServerRpc(int senderPlayerIndex, int[] sourceEntityIDs, int[] effectIndexes)
    {
        BroadcastOpponentSelectingClientRpc(senderPlayerIndex, sourceEntityIDs, effectIndexes);
    }

    [ClientRpc]
    void BroadcastOpponentSelectingClientRpc(int senderPlayerIndex, int[] sourceEntityIDs, int[] effectIndexes)
    {
        int localIndex = System.Array.IndexOf(Player.Players, GlobalSettings.Instance.localPlayer);
        if (senderPlayerIndex == localIndex) return;
        TargetingVisualEvents.RaiseOpponentTargetingStarted(sourceEntityIDs, effectIndexes);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void NotifyOpponentSelectingEndedServerRpc(int senderPlayerIndex)
    {
        BroadcastOpponentSelectingEndedClientRpc(senderPlayerIndex);
    }

    [ClientRpc]
    void BroadcastOpponentSelectingEndedClientRpc(int senderPlayerIndex)
    {
        int localIndex = System.Array.IndexOf(Player.Players, GlobalSettings.Instance.localPlayer);
        if (senderPlayerIndex == localIndex) return;
        TargetingVisualEvents.RaiseOpponentTargetingEnded();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void NotifyZoneRevealServerRpc(int zoneID)
    {
        BroadcastZoneRevealClientRpc(zoneID);
    }
    [ClientRpc]
    void BroadcastZoneRevealClientRpc(int zoneID)
    {
        ZoneManager zone = ZoneManager.AllZones.Find(z => z.Logic.ID == zoneID);
        if (zone == null) return;
        ScanButton.ReceiveRevealNotification(zone);
    }


}
