using System.Collections.Generic;
using UnityEngine;

public class ZoneCombatResolver : MonoBehaviour
{
    private Dictionary<int, int> pendingDamage = new Dictionary<int, int>();
    // Bouclier déjà "réservé" par créature PENDANT cette planification — jamais écrit sur
    // CreatureLogic.ShieldValue (la vraie consommation n'a lieu qu'à l'exécution, via
    // ConsumeShieldQueued, exactement comme aujourd'hui). Sert UNIQUEMENT à calculer, pour CHAQUE
    // coup, combien de bouclier lui est réellement disponible À CET INSTANT (avant qu'un gain réactif
    // déclenché par ce même coup, ex: "Get Mad!"/OnAllyTakeDamage, n'en rajoute) — voir
    // AddPendingCreatureDamage. Le résultat (absorbé/net) de CHAQUE coup est figé dans son
    // BattleStepRecord/AttackHitResult et diffusé tel quel aux clients ; EnqueueBattleCommands ne
    // relit plus jamais ShieldValue pour décider une absorption, seulement pour l'afficher/le
    // décrémenter dans l'ordre. Sans ça, un bouclier gagné À CHAQUE coup encaissé finit par être
    // crédité rétroactivement à TOUS les coups précédents dès qu'on relit le ShieldValue "final" une
    // fois la planification terminée (bug identifié : Tears/Fire-Forged Protector, quasi increvable
    // en combat malgré un calcul par coup qui aurait dû la tuer bien plus tôt).
    private Dictionary<int, int> pendingShieldConsumed = new Dictionary<int, int>();
    // Pertes d'aura anticipées PENDANT cette planification (voir ReservePredictedAuraLoss) — jamais
    // écrites sur CreatureLogic : la vraie perte n'a lieu qu'au rejeu, à la mort réelle de la source.
    // Attaque à retirer de GetLiveAttack ; PV max déjà retirés (pour plafonner une 2e perte d'aura).
    private Dictionary<int, int> pendingAttackLoss = new Dictionary<int, int>();
    private Dictionary<int, int> pendingMaxHealthLoss = new Dictionary<int, int>();
    // Compteur virtuel des coups déjà "consommés" par créature PENDANT cette planification — jamais
    // persisté sur CreatureLogic (la vraie décrémentation de AttacksLeftThisTurn n'a lieu qu'après coup,
    // dans EnqueueBattleCommands, rejouée identiquement sur toutes les machines). Sert uniquement à
    // savoir, en planifiant CHAQUE coup d'un Multi-Strike, s'il s'agit de son dernier coup de cette
    // bataille — voir willExhaustAttacker dans AssignSingleAttack.
    private Dictionary<int, int> _planningAttacksRemaining = new Dictionary<int, int>();

    // Créatures ayant réellement PORTÉ AU MOINS UN COUP (un BattleStepRecord a été produit — voir
    // "AucuneCible" tout en bas de AssignSingleAttack) CETTE Battle Phase (round), tous
    // resolvers/étapes confondus (voir BattleStage) — garde anti-double-attaque pour une créature
    // relocalisée en cours de round (ex: survivante d'un combat de rencontre déplacée en Base
    // principale/Base neutre — voir CommandMoveTracker.ApplyCrossingDispatch) qui serait sinon
    // planifiée une seconde fois dans une étape ultérieure. Basé sur "a réellement attaqué", PAS sur
    // "a été mise en file d'attente" : une créature mise en file dans une zone de rencontre sans la
    // moindre cible valide (ex: aucun ennemi présent dans ce croisement précis) n'a rien consommé et
    // doit garder sa chance d'attaquer dans une étape suivante — voir MarkAttackedThisRound, appelé
    // depuis BuildAutoBattleSequence uniquement quand AssignSingleAttack a effectivement produit un
    // step, jamais depuis BuildAttackQueue lui-même. Volontairement distinct de
    // AttacksLeftThisTurn/AttacksForOneTurn (voir commentaire dans BuildAttackQueue) : une créature
    // tout juste jouée ce tour peut avoir AttacksLeftThisTurn à 0 (son OnTurnStart n'a pas tourné)
    // sans que ça l'empêche de combattre. Remis à zéro une fois par round via
    // ResetAttackedThisRound, avant la planification de l'étape Rencontres.
    private static readonly HashSet<int> _creaturesAttackedThisRound = new HashSet<int>();

    public static void ResetAttackedThisRound() => _creaturesAttackedThisRound.Clear();

    static bool HasAttackedThisRound(int creatureID) => _creaturesAttackedThisRound.Contains(creatureID);

    static void MarkAttackedThisRound(int creatureID) => _creaturesAttackedThisRound.Add(creatureID);

    private static List<ZoneCombatResolver> allResolvers = new List<ZoneCombatResolver>();
    public static IReadOnlyList<ZoneCombatResolver> AllResolvers => allResolvers;
    private Dictionary<int, int> pendingBaseDamage = new Dictionary<int, int>();
    private Dictionary<int, int> pendingPlayerDamage   = new Dictionary<int, int>();

    private ZoneManager zoneView;
    private int p1FreePool;
    private int p2FreePool;
    private List<BattleStepRecord> _lastBattleSteps;

    [Header("Combat de rencontre")]
    [Tooltip("À cocher manuellement sur les zones qui servent à résoudre un \"combat de rencontre\" (deux armées qui se croisent), par opposition aux zones de base et aux zones \"classiques\" de la map. Zone_Empty n'implique rien par défaut — coche zone par zone.")]
    public bool isEncounterZone = false;

    // Files d'attaque en cours de planification (non nulles seulement pendant l'exécution de
    // BuildAutoBattleSequence) — permet à un token créé à la volée (TokenGenerationSO, via un
    // OnDeath résolu par anticipation, voir CreatureLogic.ResolvePredictedBattleDeath) de
    // rejoindre CETTE bataille au lieu d'attendre le prochain combat.
    private List<(int attack, int id)> _planningQueueP1;
    private List<(int attack, int id)> _planningQueueP2;

    // ID dédié à cette zone pour Command.RunDeferred/FlushDeferredCommands côté OnBattleStart.
    // Doit être IDENTIQUE entre le serveur et les clients (il est diffusé tel quel via
    // RecordOnBattleStartReplay/ReplayOnBattleStartEffect, voir GameNetworkManager) — impossible
    // d'utiliser IDFactory.GetLocalOnlyID() ici : ce compteur est purement local par machine (ex:
    // fantômes de déplacement créés par les interactions UI du joueur local) et diverge donc entre
    // deux clients. Dérivé à la place de l'index d'enregistrement dans allResolvers, exactement
    // comme SerializeBattleStepsForResolvers/EnqueueStageReconstructedBattleCommands corrèlent déjà les zones
    // entre pairs réseau — décalé loin de tout ID réel (toujours positif) et de la plage locale
    // de IDFactory (autour de -1 000 000) pour ne jamais entrer en collision.
    private const int ZoneDeferKeyBase = -2_000_000_000;
    private int zoneDeferKey;

    // Triplets (source, index d'effet, seed) accumulés côté serveur pendant BuildAutoBattleSequence,
    // pour chaque effet OnDeath ou OnAttack résolu par anticipation en planification
    // (CreatureLogic.ResolvePredictedBattleDeath / ResolvePredictedOnAttack) — diffusés aux clients
    // pour rejeu déterministe via EffectRegistry.Execute (même mécanisme que
    // PhaseEffectPipeline.ApplyCanonicalResolution). Pas besoin de transporter le TriggerType : l'index
    // désigne déjà un CardEffectData précis dont le Trigger est connu des deux côtés (même carte).
    //
    // OnDeath et OnAttack partagent délibérément UNE SEULE liste, dans l'ordre chronologique réel
    // d'exécution pendant la planification (pas deux listes séparées rejouées comme deux blocs) :
    // contrairement à OnBattleStart (qui se termine intégralement avant que la planification de combat
    // ne commence, donc peut être rejoué comme un bloc "toujours avant"), OnAttack et OnDeath
    // s'entrelacent à l'intérieur de AssignSingleAttack (une mort par contre-attaque peut survenir
    // pour une autre créature entre deux attaques). Rejouer ça comme deux boucles séparées risquerait
    // qu'un effet à ciblage aléatoire évalue un pool de cibles différent côté client (état de vie/mort
    // pas encore rejoué au bon moment relatif) — un désync silencieux. Une seule liste chronologique
    // élimine ce risque par construction.
    public struct PredictedTriggerReplay
    {
        public int SourceCreatureID;
        public int EffectIndex;
        public int Seed;
        public int DeferKey; // clé de report explicite — nécessaire pour OnTakeDamage, qui peut avoir
                              // plusieurs entrées pour la MÊME créature dans une même bataille (contrairement
                              // à OnDeath/OnAttack), donc la clé ne peut plus être dérivée uniquement de
                              // SourceCreatureID côté client — voir CreatureLogic.ReplayPredictedTriggerEffect
        public int EventSubjectID; // ID de la créature qui a causé ce trigger réactif (ex: l'allié qui vient
                                    // de mourir pour Rex "OnFriendlyCreatureDies") — -1 si non applicable
                                    // (OnDeath/OnAttack/OnTakeDamage, déclenchés sur la créature elle-même,
                                    // n'ont pas de sujet distinct). Voir EffectRegistry.FireListenersPredicted.
        public int TargetID; // ID de la cible de CETTE attaque (OnAttack uniquement) — -1 sinon. Nécessaire
                              // pour un ciblage AdjacentToTarget : BattleStepRecord (qui porte aussi un
                              // targetID) est diffusé dans un ClientRpc séparé et PLUS TARD que le rejeu des
                              // triggers prédits — voir GameNetworkManager.SubmitBattleAssignmentServerRpc.
        public List<(int id, int amount)> Allocation; // cibles/montants résolus par Random/RandomMeleeFirst/
                                                        // RandomSingleTarget côté serveur — liste vide si non applicable.
    }
    private static readonly List<PredictedTriggerReplay> _pendingPredictedTriggerReplays = new();

    public static void RecordPredictedTriggerReplay(int sourceCreatureID, int effectIndex, int seed, int deferKey, int eventSubjectID = -1, int targetID = -1, List<(int id, int amount)> allocation = null)
    {
        _pendingPredictedTriggerReplays.Add(new PredictedTriggerReplay
            { SourceCreatureID = sourceCreatureID, EffectIndex = effectIndex, Seed = seed, DeferKey = deferKey, EventSubjectID = eventSubjectID, TargetID = targetID, Allocation = allocation ?? new() });
    }

    // Consommé une seule fois par SubmitBattleAssignmentServerRpc, juste après que toute la
    // planification (tous resolvers) soit terminée, pour sérialiser vers les clients.
    public static List<PredictedTriggerReplay> DrainPredictedTriggerReplays()
    {
        List<PredictedTriggerReplay> snapshot = new List<PredictedTriggerReplay>(_pendingPredictedTriggerReplays);
        _pendingPredictedTriggerReplays.Clear();
        return snapshot;
    }

    // Vrai pendant l'appel synchrone à EffectRegistry.Execute fait par CreatureLogic.ResolvePredictedBattleDeath/
    // ResolvePredictedOnAttack côté serveur réseau (entre BeginResolvingPredictedTrigger/EndResolvingPredictedTrigger).
    // Sert de signal à TokenGenerationSO.Execute : un token créé PENDANT cette fenêtre doit rejoindre la liste
    // ci-dessous (PredictedTokenSpawn), rejouée au bon endroit relatif dans la boucle côté client, plutôt que
    // d'être diffusé tout de suite via GameNetworkManager.BroadCastTokenToZone.
    //
    // Raison : ce ClientRpc immédiat arrive, pour TOUS les tokens de TOUTE la bataille, AVANT le seul et unique
    // ApplyCanonicalBattleAssignmentClientRpc envoyé une fois la planification entière terminée (voir
    // SubmitBattleAssignmentServerRpc) — donc quand la boucle de rejeu des triggers prédits démarre côté client,
    // TOUS ces tokens existent déjà dans playedCards.Creatures, y compris ceux qui, dans l'ordre chronologique
    // réel de planification, n'auraient dû apparaître qu'APRÈS le trigger en cours de rejeu. Un effet à ciblage
    // aléatoire (ex: Flag-Bearer "Inspire", RandomSingleTarget) voit alors un pool de cibles plus large côté
    // client que côté hôte (constaté : hôte "1 target — Queen 1", client "2 target(s) — Queen 1, Zergling
    // Token"), avec un tirage RNG identique mais sur un intervalle différent → cible différente des deux côtés.
    public static bool IsResolvingPredictedTrigger { get; private set; }
    public static void BeginResolvingPredictedTrigger() => IsResolvingPredictedTrigger = true;
    public static void EndResolvingPredictedTrigger() => IsResolvingPredictedTrigger = false;

    // Un spawn de token (TokenGenerationSO, placement ToZone) survenu pendant la résolution d'un trigger
    // prédit — tagué par (SourceEntityID, EffectIndex), la même identité que l'entrée PredictedTriggerReplay
    // qui l'a causé (un attaquant/une créature ne déclenche ce trigger précis qu'une fois par bataille, donc
    // cette paire est unique dans la liste). Rejoué côté client juste après ReplayPredictedTriggerEffect pour
    // cette même entrée, dans ApplyCanonicalBattleAssignmentClientRpc — jamais via un ClientRpc séparé.
    public struct PredictedTokenSpawn
    {
        public int SourceEntityID;
        public int EffectIndex;
        public int PlayerIndex;
        public int CardID;
        public int CreatureID;
        public int TablePos;
        public int BaseID;
        public int DeferKey;
    }
    private static readonly List<PredictedTokenSpawn> _pendingPredictedTokenSpawns = new();

    public static void RecordPredictedTokenSpawn(int sourceEntityID, int effectIndex, int playerIndex, int cardID, int creatureID, int tablePos, int baseID, int deferKey)
    {
        _pendingPredictedTokenSpawns.Add(new PredictedTokenSpawn
        {
            SourceEntityID = sourceEntityID, EffectIndex = effectIndex, PlayerIndex = playerIndex,
            CardID = cardID, CreatureID = creatureID, TablePos = tablePos, BaseID = baseID, DeferKey = deferKey
        });
    }

    public static List<PredictedTokenSpawn> DrainPredictedTokenSpawns()
    {
        List<PredictedTokenSpawn> snapshot = new List<PredictedTokenSpawn>(_pendingPredictedTokenSpawns);
        _pendingPredictedTokenSpawns.Clear();
        return snapshot;
    }

    // Triplets accumulés côté serveur pendant ResolveOnBattleStartEffects, pour rejeu déterministe
    // côté client (même mécanisme que PredictedTriggerReplay ci-dessus).
    public struct OnBattleStartReplay
    {
        public int ZoneDeferKey;
        public int SourceID;
        public int EffectIndex;
        public int Seed;
        public List<(int id, int amount)> Allocation; // cibles/montants résolus par Random/RandomMeleeFirst/
                                                        // RandomSingleTarget côté serveur — liste vide si non applicable.
    }
    private static readonly List<OnBattleStartReplay> _pendingOnBattleStartReplays = new();

    public static void RecordOnBattleStartReplay(int zoneDeferKey, int sourceID, int effectIndex, int seed, List<(int id, int amount)> allocation)
    {
        _pendingOnBattleStartReplays.Add(new OnBattleStartReplay
            { ZoneDeferKey = zoneDeferKey, SourceID = sourceID, EffectIndex = effectIndex, Seed = seed, Allocation = allocation ?? new() });
    }

    public static List<OnBattleStartReplay> DrainOnBattleStartReplays()
    {
        List<OnBattleStartReplay> snapshot = new List<OnBattleStartReplay>(_pendingOnBattleStartReplays);
        _pendingOnBattleStartReplays.Clear();
        return snapshot;
    }

    // Appelé côté client par GameNetworkManager.ApplyCanonicalBattleAssignmentClientRpc.
    public static void ReplayOnBattleStartEffect(int zoneDeferKey, int sourceID, int effectIndex, int seed, List<(int id, int amount)> allocation)
    {
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(sourceID, out CreatureLogic c))
            c.ReplayBattleStartEffect(zoneDeferKey, effectIndex, seed, allocation);
    }

    private enum TargetKind { Creature, Base, Player }
    private struct BattleStepRecord
    {
        public int attackerID;
        public int targetID;
        public TargetKind targetKind;
        public int damage;
        public int targetOwnerPlayerID;
        // Cibles secondaires (AttackModifierSO) déjà résolues pendant la planification — ID + dégât brut.
        // Diffusées telles quelles à tous les clients ; EnqueueBattleCommands ne recalcule plus aucun ciblage.
        public List<AttackHitResult> secondaryHits;
        // Contre-dégâts subis par l'attaquant, figés au moment de la planification (même valeur que
        // celle passée à AddPendingCreatureDamage pour prédire une éventuelle mort de l'attaquant).
        // EnqueueBattleCommands ne relit plus target.Attack en direct : ça appliquerait à une contre-
        // attaque déjà causalement figée un buff/debuff survenu plus tard dans la séquence planifiée.
        public int counterDamage;
        // AttacksLeftThisTurn de l'attaquant tombera à 0 après cette attaque — transporté ici pour être
        // rejoué identiquement sur toutes les machines (voir EnqueueBattleCommands), jamais décidé
        // localement côté client : la mutation elle-même n'a lieu qu'en planification côté serveur.
        public bool attackerExhausted;
        // Bouclier de la cible / de l'attaquant absorbé par CE coup précisément, précalculé pendant la
        // planification (voir pendingShieldConsumed / AddPendingCreatureDamage) avec le bouclier
        // RESTANT au moment exact de ce coup — jamais recalculé à l'exécution à partir de ShieldValue
        // "final" (voir EnqueueBattleCommands), pour ne pas créditer rétroactivement un bouclier gagné
        // après coup (ex: "Get Mad!"/OnAllyTakeDamage, qui mute ShieldValue en cours de planification).
        public int shieldAbsorbed;
        public int attackerShieldAbsorbed;
        // Issue de CE coup décidée par la planification (IsEffectivelyDead juste après le coup), diffusée
        // à toutes les machines et appliquée telle quelle au rejeu (EnqueueBattleCommands) au lieu d'être
        // redéduite des PV live : un effet résolu en pleine planification (buff/dégâts OnDeath, OnAttack...)
        // modifie les PV tout de suite, donc un recalcul au rejeu pouvait tuer une unité que la
        // planification voyait survivre — mort jamais prédite, OnDeath résolu côté hôte seulement
        // (bug Hill King) — ou l'inverse. secondaryDies est parallèle à secondaryHits.
        public bool targetDies;
        public bool attackerDies;
        public List<bool> secondaryDies;
    }

    // Index d'enregistrement de ce resolver dans allResolvers, capturé une fois pour toutes ici (avant
    // l'ajout à la liste) — sert de composante à OnTakeDamageDeferKey pour qu'aucune clé ne collisionne
    // entre deux zones. Doit être IDENTIQUE entre le serveur et les clients, comme zoneDeferKey juste en
    // dessous, pour la même raison (ordre d'enregistrement des resolvers supposé déterministe et
    // identique sur toutes les machines).
    private int resolverIndex;

    void Awake()
    {
        zoneView = GetComponent<ZoneManager>();
        resolverIndex = allResolvers.Count;
        zoneDeferKey = ZoneDeferKeyBase - allResolvers.Count;
        allResolvers.Add(this);
    }

    public bool HasPossibleCombat()
    {
        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;
        int p1CreatureCount = GetCreaturesInMyZone(p1, zoneView).Count;
        int p2CreatureCount = GetCreaturesInMyZone(p2, zoneView).Count;
        // Quand le bâtiment de pX n'est pas actif (voir Player.HomeBuildingActive), sa zone de départ
        // (MainPArea) n'est plus un point de vie destructible (voir AssignSingleAttack) — un ennemi non
        // bloqué là-bas ne doit donc plus compter comme "combat possible" ici non plus, sinon les popups
        // OnBattleStart et le focus caméra (ZoneBattleStartRevealCommand) se déclenchent pour un combat
        // qui n'aura jamais lieu.
        bool p1MainZoneIsLiveTarget = p1.HomeBuildingActive && zoneView.subZones.Contains(p1.MainPArea);
        bool p2MainZoneIsLiveTarget = p2.HomeBuildingActive && zoneView.subZones.Contains(p2.MainPArea);

        if (
            (p1CreatureCount > 0 && p2CreatureCount > 0) ||
            (FindDefenderBaseInZone(p1) != null && p2CreatureCount > 0) ||
            (FindDefenderBaseInZone(p2) != null && p1CreatureCount > 0) ||
            (p1MainZoneIsLiveTarget && p2CreatureCount > 0) ||
            (p2MainZoneIsLiveTarget && p1CreatureCount > 0)
        )
            return true;
        return false;
    }

    // Planification uniquement — jamais d'enqueue ici, dans AUCUN mode. Nécessaire pour que
    // ComputeRoundOutcome() puisse lire pendingPlayerDamage de TOUS les resolvers (base principale
    // des deux joueurs comprise) avant qu'aucune commande ne mute Health : si l'enqueue restait
    // interfolé ici comme avant en solo, un resolver plus tardif dans la boucle observerait un
    // Health déjà partiellement appliqué par un resolver précédent du même round (double décompte).
    // Voir TurnManager.DelayedBattleStart (solo) / GameNetworkManager.SubmitBattleAssignmentServerRpc
    // (réseau) pour l'enqueue, désormais toujours séparé et postérieur à la planification complète.
    public void OnBattlePhaseStart()
    {
        pendingDamage.Clear();
        pendingShieldConsumed.Clear();
        pendingAttackLoss.Clear();
        pendingMaxHealthLoss.Clear();
        pendingBaseDamage.Clear();
        pendingPlayerDamage.Clear();
        _planningAttacksRemaining.Clear();
        p1FreePool = 0;
        p2FreePool = 0;

        bool isPlanner = !NetworkSessionData.IsNetworkSession || Unity.Netcode.NetworkManager.Singleton.IsServer;
        if (!isPlanner) return;

        ResolveOnBattleStartEffects();
        _lastBattleSteps = BuildAutoBattleSequence(zoneView);
    }

    // Résout OnBattleStart pour toutes les créatures des DEUX joueurs présentes dans cette zone,
    // avant toute planification — pour que buffs, dégâts, boucliers et morts éventuelles soient déjà
    // reflétés dans l'état lu par BuildAutoBattleSequence.
    void ResolveOnBattleStartEffects()
    {
        if (!HasPossibleCombat()) return;

        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;

        List<CreatureLogic> creatures = new List<CreatureLogic>();
        creatures.AddRange(GetCreaturesInMyZone(p1, zoneView));
        creatures.AddRange(GetCreaturesInMyZone(p2, zoneView));
        foreach (CreatureLogic c in creatures)
            c.ResolveBattleStartEffects(zoneDeferKey);
    }

    public void OnBattlePhaseEnd()
    {
        foreach (PlayerArea pa in zoneView.subZones)
            if (pa.tableVisual != null)
                new RefreshTableSlotsCommand(pa.tableVisual).AddToQueue();

        pendingDamage.Clear();
        pendingShieldConsumed.Clear();
        pendingAttackLoss.Clear();
        pendingMaxHealthLoss.Clear();
        ClearAllIndicators();
    }

    PlayerArea FindAreaForCreature(CreatureLogic creature)
    {
        foreach (PlayerArea pa in zoneView.subZones)
            if (pa.baseID == creature.BaseID)
                return pa;
        return null;
    }

    List<BattleStepRecord> BuildAutoBattleSequence(ZoneManager zone)
    {
        List<BattleStepRecord> steps = new();
        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;

        List<(int attack, int id)> queue1 = BuildAttackQueue(p1, zone);
        List<(int attack, int id)> queue2 = BuildAttackQueue(p2, zone);
        _planningQueueP1 = queue1;
        _planningQueueP2 = queue2;

        int p1ZoneUnitCount = GetCreaturesInMyZone(p1, zone).Count;
        int p2ZoneUnitCount = GetCreaturesInMyZone(p2, zone).Count;
        bool p1Turn = p1ZoneUnitCount != p2ZoneUnitCount
            ? p1ZoneUnitCount > p2ZoneUnitCount
            : UnityEngine.Random.value < 0.5f;
        Debug.Log($"[Sequence:{zoneView.name}] Commence : {(p1Turn ? p1.name : p2.name)} | queue1={queue1.Count} queue2={queue2.Count}");
        int i1 = 0, i2 = 0, stepNum = 0;

        while (true)
        {
            // pendingDamage inclut désormais les dégâts de zone prédits (ReserveModifierSplashDamage),
            // donc ce skip peut être causé par une mort prédite via un modificateur, pas seulement par
            // une attaque directe classique.
            while (i1 < queue1.Count && IsAttackerDead(queue1[i1]))
            {
                pendingDamage.TryGetValue(queue1[i1].id, out int reserved);
                Debug.Log($"  [Skip mort:{zoneView.name}] {p1.name} — ID:{queue1[i1].id} (pendingDamage:{reserved}) — tour annulé");
                i1++;
            }
            while (i2 < queue2.Count && IsAttackerDead(queue2[i2]))
            {
                pendingDamage.TryGetValue(queue2[i2].id, out int reserved);
                Debug.Log($"  [Skip mort:{zoneView.name}] {p2.name} — ID:{queue2[i2].id} (pendingDamage:{reserved}) — tour annulé");
                i2++;
            }

            bool p1CanAct = i1 < queue1.Count;
            bool p2CanAct = i2 < queue2.Count;

            if (!p1CanAct && !p2CanAct) break;

            // Un Multi-Strike (plusieurs entrées consécutives de MÊME id, voir BuildAttackQueue) résout
            // TOUS ses coups d'affilée avant de rendre la main à l'adversaire, au lieu d'alterner un
            // coup sur deux — ré-vérifie IsAttackerDead entre chaque coup du même attaquant : une
            // contre-attaque qui le tue en cours de rafale doit annuler ses coups restants.
            if (p1CanAct && (!p2CanAct || p1Turn))
            {
                int burstID = queue1[i1].id;
                while (i1 < queue1.Count && queue1[i1].id == burstID && !IsAttackerDead(queue1[i1]))
                {
                    (int attack, int id) attacker = queue1[i1++];
                    (int overflow, BattleStepRecord? step) = AssignSingleAttack(attacker, p2, zone, steps.Count);
                    p1FreePool += overflow;
                    if (step.HasValue)
                    {
                        steps.Add(step.Value);
                        // Marqué seulement ici (attaque réellement portée, pas juste mise en file) —
                        // voir _creaturesAttackedThisRound / BuildAttackQueue.
                        MarkAttackedThisRound(attacker.id);
                        Debug.Log($"[DiagQueue:{zoneView.name}] MarkAttackedThisRound — attaquant ID:{attacker.id}");
                    }
                }
            }
            else
            {
                int burstID = queue2[i2].id;
                while (i2 < queue2.Count && queue2[i2].id == burstID && !IsAttackerDead(queue2[i2]))
                {
                    (int attack, int id) attacker = queue2[i2++];
                    (int overflow, BattleStepRecord? step) = AssignSingleAttack(attacker, p1, zone, steps.Count);
                    p2FreePool += overflow;
                    if (step.HasValue)
                    {
                        steps.Add(step.Value);
                        MarkAttackedThisRound(attacker.id);
                        Debug.Log($"[DiagQueue:{zoneView.name}] MarkAttackedThisRound — attaquant ID:{attacker.id}");
                    }
                }
            }

            if (p1CanAct && p2CanAct) p1Turn = !p1Turn;
            else p1Turn = p1CanAct;
        }
        Debug.Log($"[Sequence:{zoneView.name}] {steps.Count} step(s) générés au total");
        _planningQueueP1 = null;
        _planningQueueP2 = null;
        return steps;
    }

    // Appelé par TokenGenerationSO quand un token est créé à la volée. Si ce baseID est
    // couvert par un resolver actuellement en train de planifier sa bataille (voir
    // BuildAutoBattleSequence / _planningQueueP1-P2), le token rejoint la file d'attaque de son
    // propriétaire pour pouvoir encore attaquer dans cette même bataille.
    public static void NotifyCreatureAddedDuringPlanning(CreatureLogic creature)
    {
        if (creature.Attack <= 0) return;
        ZoneCombatResolver resolver = FindForBase(creature.BaseID);
        if (resolver == null) return;

        // Pas de marquage _creaturesAttackedThisRound ici : ce token vient d'apparaître, il n'a
        // encore rien attaqué. Il sera marqué comme les autres, au moment où BuildAutoBattleSequence
        // constatera un step réellement produit pour lui (voir MarkAttackedThisRound).
        List<(int attack, int id)> queue =
            creature.owner == GlobalSettings.Instance.LowPlayer ? resolver._planningQueueP1
            : creature.owner == GlobalSettings.Instance.TopPlayer ? resolver._planningQueueP2
            : null;
        for (int n = 0; n < Mathf.Max(1, creature.AttacksForOneTurn); n++)
            queue?.Add((creature.Attack, creature.UniqueCreatureID));
    }

    // Ordre : mêlée créatures → non-mêlée créatures
    List<(int attack, int id)> BuildAttackQueue(Player player, ZoneManager zone)
    {
        List<(int, int)> result = new();
        List<CreatureLogic> creatures = GetCreaturesInMyZone(player, zone);

        // Multi-Strike (CardAsset.AttacksForOneTurn > 1) : une entrée par coup, pas par créature —
        // la créature revient dans la file d'attente autant de fois que son nombre de coups par
        // bataille. Volontairement basé sur AttacksForOneTurn (le maximum par tour) et non sur
        // AttacksLeftThisTurn (souvent déjà à 0 pour une créature tout juste jouée ce tour — son
        // OnTurnStart n'a pas encore tourné — sans que ça l'empêche de combattre) : voir
        // _creaturesAttackedThisRound (haut de fichier) pour la vraie garde anti-double-attaque —
        // lecture seule ici, le marquage n'a lieu qu'une fois l'attaque réellement résolue (voir
        // MarkAttackedThisRound dans BuildAutoBattleSequence).
        // DIAGNOSTIC TEMPORAIRE — à retirer une fois la régression "arrivée de rencontre" comprise.
        foreach (CreatureLogic dc in creatures)
            Debug.Log($"[DiagQueue:{zoneView.name}] {dc.DisplayName}(ID:{dc.UniqueCreatureID}) owner={player.name} Attack={dc.Attack} IsMelee={dc.IsMelee} HasAttackedThisRound={HasAttackedThisRound(dc.UniqueCreatureID)} AttacksLeftThisTurn={dc.AttacksLeftThisTurn} AttacksForOneTurn={dc.AttacksForOneTurn} BaseID={dc.BaseID}");

        foreach (CreatureLogic c in creatures)
            if (c.IsMelee && c.Attack > 0 && !HasAttackedThisRound(c.UniqueCreatureID))
                for (int n = 0; n < Mathf.Max(1, c.AttacksForOneTurn); n++)
                    result.Add((c.Attack, c.UniqueCreatureID));
        foreach (CreatureLogic c in creatures)
            if (!c.IsMelee && c.Attack > 0 && !HasAttackedThisRound(c.UniqueCreatureID))
                for (int n = 0; n < Mathf.Max(1, c.AttacksForOneTurn); n++)
                    result.Add((c.Attack, c.UniqueCreatureID));

        return result;
    }

    // Retourne le surplus de dégâts non placés (overflow) et la description de l'attaque pour animation
    // Priorité : mêlée créature → ranged créature → base → joueur
    (int, BattleStepRecord?) AssignSingleAttack((int attack, int id) attacker, Player defender, ZoneManager zone, int stepIndex)
    {
        // Lecture live (pas la valeur figée dans le tuple attacker au tout début de la zone) :
        // si cet attaquant a été buffé par un OnDeath résolu plus tôt dans cette même
        // planification (voir AddPendingCreatureDamage), les dégâts qu'il inflige doivent déjà
        // le refléter tant qu'il n'a pas encore été traité par cette méthode.
        int dmg = GetLiveAttack(attacker.id, attacker.attack);
        List<CreatureLogic> creatures = GetCreaturesInMyZone(defender, zone);

        // Résolution de OnAttack : appelée juste avant chaque `return` qui produit effectivement un
        // BattleStepRecord (jamais dans le cas "AucuneCible" tout en bas — aucune Command ne serait
        // alors jamais créée pour flush le bucket différé, qui resterait bloqué pour toujours, comme
        // le bug déjà documenté ci-dessous pour EnqueueBattleCommands([])). Placé APRÈS la lecture de
        // `dmg`, donc un buff OnAttack n'affecte jamais les dégâts de CETTE frappe (même philosophie
        // que OnDeath, qui n'affecte jamais l'évènement qui l'a déclenché) — seulement les suivantes.
        CreatureLogic attackerLogic = CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attacker.id, out CreatureLogic al) ? al : null;

        // Lecture seule ici : la mutation réelle de AttacksLeftThisTurn a lieu dans EnqueueBattleCommands,
        // rejouée identiquement sur toutes les machines à partir de ce flag transporté — jamais ici
        // (planification côté serveur uniquement), sinon désync des clients (même principe que
        // Health/Attack ailleurs dans ce fichier).
        // Basé sur attacksForOneTurn (nombre de coups par bataille), PAS sur la valeur live de
        // AttacksLeftThisTurn : celle-ci n'est décrémentée qu'après la planification complète (voir
        // EnqueueBattleCommands), donc resterait figée à la même valeur pour TOUS les coups d'un
        // Multi-Strike si on la lisait ici. _planningAttacksRemaining simule la décrémentation
        // coup par coup, uniquement pour la durée de cette planification.
        bool willExhaustAttacker = false;
        if (attackerLogic != null)
        {
            if (!_planningAttacksRemaining.TryGetValue(attacker.id, out int remainingStrikes))
                remainingStrikes = Mathf.Max(1, attackerLogic.AttacksForOneTurn);
            remainingStrikes = Mathf.Max(0, remainingStrikes - 1);
            _planningAttacksRemaining[attacker.id] = remainingStrikes;
            willExhaustAttacker = remainingStrikes == 0;
        }

        // Un attaquant Melee qui n'est pas lui-même Flying ne peut pas cibler une créature Flying.
        bool attackerIsGroundedMelee = IsMeleeAttacker(attacker.id) && !(attackerLogic?.IsFlying ?? false);
        // Un attaquant Melee Flying ne subit pas la contre-attaque d'une créature Melee au sol (non-Flying) :
        // elle ne peut pas l'atteindre en retour. Elle continue en revanche de subir la riposte d'une
        // créature Ranged ou d'une autre créature Flying (voir Tier 3 / Tier 2 selon le cas).
        bool attackerIsFlyingMelee = IsMeleeAttacker(attacker.id) && (attackerLogic?.IsFlying ?? false);

        // Tier 2 : créatures mêlée
        List<CreatureLogic> eligibleMeleeCreatures = new List<CreatureLogic>();
        foreach (CreatureLogic t in creatures)
        {
            if (!t.IsMelee || IsEffectivelyDead(t)) continue;
            if (t.IsFlying && attackerIsGroundedMelee) continue;
            eligibleMeleeCreatures.Add(t);
        }
        if (eligibleMeleeCreatures.Count > 0)
        {
            CreatureLogic t = eligibleMeleeCreatures[UnityEngine.Random.Range(0, eligibleMeleeCreatures.Count)];
            pendingDamage.TryGetValue(t.UniqueCreatureID, out int existing);
            // Bouclier RESTANT au sens de cette planification (pas t.ShieldValue brut) : un gain
            // réactif survenu APRÈS que d'autres coups aient déjà été assignés à t ne doit pas
            // rouvrir de la capacité déjà consommée par ces coups-là — voir pendingShieldConsumed.
            pendingShieldConsumed.TryGetValue(t.UniqueCreatureID, out int shieldAlreadyConsumed2);
            int shieldRemaining2 = Mathf.Max(0, t.ShieldValue - shieldAlreadyConsumed2);
            int assign = Mathf.Min(dmg, t.Health - existing + shieldRemaining2);
            int shieldAbsorbed2 = AddPendingCreatureDamage(t.UniqueCreatureID, assign, stepIndex, 0);
            // Debug.Log($"[Assign:{zoneView.name}][Tier2:CréatureMêlée] attaquant={attacker.id} cible créat={t.UniqueCreatureID}({t.DisplayName}) dégâts={assign} overflow={dmg - assign}");
            int counter2 = 0;
            int attackerShieldAbsorbed2 = 0;
            bool groundMeleeCantReachFlying = attackerIsFlyingMelee && !t.IsFlying;
            if (IsMeleeAttacker(attacker.id) && !groundMeleeCantReachFlying)
            {
                counter2 = GetLiveAttack(t.UniqueCreatureID, t.Attack);
                attackerShieldAbsorbed2 = AddPendingCreatureDamage(attacker.id, counter2, stepIndex, 1);
            }
            List<AttackHitResult> tier2SecondaryHits = ResolveAndReserveModifierHits(attacker.id, t, stepIndex);
            attackerLogic?.ResolvePredictedOnAttack(t);
            return (dmg - assign, WithPlannedDeaths(new BattleStepRecord { attackerID = attacker.id, targetID = t.UniqueCreatureID, targetKind = TargetKind.Creature, damage = assign, targetOwnerPlayerID = defender.PlayerID, secondaryHits = tier2SecondaryHits, counterDamage = counter2, attackerExhausted = willExhaustAttacker, shieldAbsorbed = shieldAbsorbed2, attackerShieldAbsorbed = attackerShieldAbsorbed2 }, t, attackerLogic));
        }

        // Tier 3 : créatures ranged
        List<CreatureLogic> eligibleRangedCreatures = new List<CreatureLogic>();
        foreach (CreatureLogic t in creatures)
        {
            if (t.IsMelee || IsEffectivelyDead(t)) continue;
            if (t.IsFlying && attackerIsGroundedMelee) continue;
            eligibleRangedCreatures.Add(t);
        }
        if (eligibleRangedCreatures.Count > 0)
        {
            CreatureLogic t = eligibleRangedCreatures[UnityEngine.Random.Range(0, eligibleRangedCreatures.Count)];
            pendingDamage.TryGetValue(t.UniqueCreatureID, out int existing);
            // Bouclier RESTANT au sens de cette planification — voir commentaire équivalent au Tier 2.
            pendingShieldConsumed.TryGetValue(t.UniqueCreatureID, out int shieldAlreadyConsumed3);
            int shieldRemaining3 = Mathf.Max(0, t.ShieldValue - shieldAlreadyConsumed3);
            int assign = Mathf.Min(dmg, t.Health - existing + shieldRemaining3);
            int shieldAbsorbed3 = AddPendingCreatureDamage(t.UniqueCreatureID, assign, stepIndex, 0);
            // Debug.Log($"[Assign:{zoneView.name}][Tier3:CréatureRanged] attaquant={attacker.id} cible créat={t.UniqueCreatureID}({t.DisplayName}) dégâts={assign} overflow={dmg - assign}");
            int counter3 = 0;
            int attackerShieldAbsorbed3 = 0;
            if (IsMeleeAttacker(attacker.id))
            {
                counter3 = GetLiveAttack(t.UniqueCreatureID, t.Attack);
                attackerShieldAbsorbed3 = AddPendingCreatureDamage(attacker.id, counter3, stepIndex, 1);
            }
            List<AttackHitResult> tier3SecondaryHits = ResolveAndReserveModifierHits(attacker.id, t, stepIndex);
            attackerLogic?.ResolvePredictedOnAttack(t);
            return (dmg - assign, WithPlannedDeaths(new BattleStepRecord { attackerID = attacker.id, targetID = t.UniqueCreatureID, targetKind = TargetKind.Creature, damage = assign, targetOwnerPlayerID = defender.PlayerID, secondaryHits = tier3SecondaryHits, counterDamage = counter3, attackerExhausted = willExhaustAttacker, shieldAbsorbed = shieldAbsorbed3, attackerShieldAbsorbed = attackerShieldAbsorbed3 }, t, attackerLogic));
        }

        BaseLogic defenderBase = FindDefenderBaseInZone(defender);
        if (defenderBase != null)
        {
            pendingBaseDamage.TryGetValue(defenderBase.ID, out int existing);
            pendingBaseDamage[defenderBase.ID] = existing + dmg;
            // Debug.Log($"[Battle→Base:{zoneView.name}] attaquant={attacker.id} cible base={defenderBase.ID} ({defenderBase.DisplayName}) dégâts={dmg}");
            attackerLogic?.ResolvePredictedOnAttack(defenderBase);
            return (0, new BattleStepRecord { attackerID = attacker.id, targetID = defenderBase.ID, targetKind = TargetKind.Base, damage = dmg, targetOwnerPlayerID = defender.PlayerID, attackerExhausted = willExhaustAttacker });
        }
        // Quand le bâtiment de defender n'est pas actif (voir Player.HomeBuildingActive), la base
        // principale n'est plus un point de vie destructible dans sa zone de départ : le seul moyen de
        // blesser ce joueur est de tuer ses unités-bases (déjà couvertes plus haut, tiers 2/3, comme
        // n'importe quelle créature, où qu'elles se trouvent). Un attaquant sans cible dans cette zone
        // n'inflige donc plus rien ici.
        if (defender.HomeBuildingActive && zoneView.subZones.Contains(defender.MainPArea))
        {
            pendingPlayerDamage.TryGetValue(defender.PlayerID, out int existing);
            pendingPlayerDamage[defender.PlayerID] = existing + dmg;
            // Debug.Log($"[Battle→Player:{zoneView.name}] attaquant={attacker.id} cible joueur={defender.name} dégâts={dmg}");
            attackerLogic?.ResolvePredictedOnAttack(defender);
            return (0, new BattleStepRecord { attackerID = attacker.id, targetID = defender.PlayerID, targetKind = TargetKind.Player, damage = dmg, targetOwnerPlayerID = defender.PlayerID, attackerExhausted = willExhaustAttacker });
        }
        Debug.Log($"[DiagQueue][Assign:{zoneView.name}][AucuneCible] attaquant={attacker.id} dégâts={dmg} perdus — aucune cible éligible (créatures/base/joueur)");
        return (dmg, null);
    }

    // PV d'une unité après un coup au rejeu, selon l'issue décidée par la planification (voir
    // BattleStepRecord.targetDies) plutôt que selon ses seuls PV live. Signale tout écart : il vient d'un
    // effet résolu en pleine planification qui a modifié ses PV avant que les coups ne soient rejoués.
    int PlannedHealthAfter(CreatureLogic creature, int effectiveDamage, bool plannedDeath, string role)
    {
        if (creature.IsPendingDeath) return 0;
        int live = creature.Health - effectiveDamage;
        if (plannedDeath)
        {
            if (live > 0)
                Debug.LogWarning($"[Enqueue:{zoneView.name}] {creature.DisplayName}(ID:{creature.UniqueCreatureID}) ({role}) — tuée par la planification mais resterait à {live} PV en live : mort appliquée.");
            return 0;
        }
        if (live <= 0)
        {
            Debug.LogWarning($"[Enqueue:{zoneView.name}] {creature.DisplayName}(ID:{creature.UniqueCreatureID}) ({role}) — survit selon la planification mais tomberait à {live} PV en live : PV ramenés à 1.");
            return 1;
        }
        return live;
    }

    // Fige dans le step l'issue décidée par la planification pour chaque unité touchée par CE coup (voir
    // BattleStepRecord.targetDies) — appelé une fois le coup entier résolu (cible, riposte, touchés
    // secondaires, OnAttack).
    BattleStepRecord WithPlannedDeaths(BattleStepRecord step, CreatureLogic target, CreatureLogic attackerLogic)
    {
        step.targetDies = IsEffectivelyDead(target);
        step.attackerDies = attackerLogic != null && IsEffectivelyDead(attackerLogic);
        step.secondaryDies = new List<bool>();
        if (step.secondaryHits != null)
            foreach (AttackHitResult hit in step.secondaryHits)
                step.secondaryDies.Add(CreatureLogic.CreaturesCreatedThisGame.TryGetValue(hit.TargetUniqueID, out CreatureLogic secondary)
                    && IsEffectivelyDead(secondary));
        return step;
    }

    // Résout, UNE SEULE FOIS pendant la planification (côté serveur en réseau), les cibles secondaires
    // de tous les AttackModifierSO (Lateral/Row/Cone/Piercing/Circular/Ricochet) de cet attaquant, et
    // les injecte dans pendingDamage — le même dictionnaire déjà utilisé pour l'anti-surkill
    // (AssignSingleAttack) et pour prédire les morts en séquence (IsEffectivelyDead / IsAttackerDead),
    // qui sert ici aussi de "isDead" pour la résolution des modificateurs eux-mêmes (une cible tuée par
    // un attaquant précédent dans cette même passe ne peut plus être retouchée/reciblée).
    // Le résultat figé (ID + dégât brut) est stocké dans le BattleStepRecord et diffusé à tous les
    // clients : EnqueueBattleCommands ne fait plus AUCUNE décision de ciblage, il ne fait qu'appliquer
    // les dégâts aux IDs déjà donnés — élimine tout risque de désync sur "qui se fait toucher".
    List<AttackHitResult> ResolveAndReserveModifierHits(int attackerID, CreatureLogic mainTarget, int stepIndex)
    {
        List<AttackHitResult> allHits = new List<AttackHitResult>();
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic attackerCreature)) return allHits;
        if (attackerCreature.AttackModifiers == null) return allHits;

        // Un attaquant Melee au sol ne peut pas toucher de Flying, même comme cible secondaire
        // d'un modificateur (Piercing/Row/Circular/...) — même règle que pour la cible principale
        // (voir attackerIsGroundedMelee dans AssignSingleAttack).
        bool attackerIsGroundedMelee = attackerCreature.IsMelee && !attackerCreature.IsFlying;

        foreach (AttackModifierSO mod in attackerCreature.AttackModifiers)
        {
            if (mod == null) continue; // slot vide/cassé — voir le même garde-fou dans EnqueueBattleCommands
            List<AttackHitResult> resolved = mod.ResolveTargets(attackerCreature, mainTarget, IsEffectivelyDead);
            if (resolved == null) continue;

            foreach (AttackHitResult hit in resolved)
            {
                if (attackerIsGroundedMelee
                    && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(hit.TargetUniqueID, out CreatureLogic hitCreature)
                    && hitCreature.IsFlying)
                    continue;

                int secAbsorbed = AddPendingCreatureDamage(hit.TargetUniqueID, hit.Damage, stepIndex, 2 + allHits.Count);
                allHits.Add(new AttackHitResult(hit.TargetUniqueID, hit.Damage, hit.HealthAfter, secAbsorbed));
                Debug.Log($"[Resolve:{zoneView.name}] {mod.GetType().Name} — attaquant={attackerID}({attackerCreature.DisplayName}) → cible={hit.TargetUniqueID} dégâts={hit.Damage} absorbé={secAbsorbed}");
            }
        }
        return allHits;
    }

    void EnqueueBattleCommands(List<BattleStepRecord> steps)
    {
        // Les popups OnBattleStart différés (voir ResolveOnBattleStartEffects/zoneDeferKey) doivent
        // être enqueue ICI, de façon synchrone et AVANT les commandes d'attaque ci-dessous — sinon
        // ils se retrouvent après elles dans la file (voir ZoneBattleStartRevealCommand). La barrière
        // ne fait qu'attendre l'arrivée de la BattleCam sur la zone avant de laisser la LECTURE de la
        // file continuer ; elle ne retarde jamais l'ajout des popups eux-mêmes.
        // Debug.Log($"[DBG][EnqueueBattleCommands] zone={zoneView.name} zoneDeferKey={zoneDeferKey} HasDeferredCommands={Command.HasDeferredCommands(zoneDeferKey)}");
        if (Command.HasDeferredCommands(zoneDeferKey))
        {
            new ZoneBattleStartRevealCommand(zoneView.transform.position).AddToQueue();
            Command.FlushDeferredCommands(zoneDeferKey);
        }
        RecordInvasions(steps);
        // Bases mortes pendant CETTE zone — animation de mort jouée UNE SEULE FOIS, après la boucle
        // ci-dessous, jamais immédiatement dans le switch : un round sans limite anti-overkill (voir
        // AssignSingleAttack, branches base/joueur) peut infliger plusieurs coups fatals successifs à
        // la même base dans la même zone, et l'animation ne doit rejouer ni pour chacun d'eux ni avant
        // que le reste des combats de la zone n'ait fini de s'animer.
        HashSet<int> diedHomeBasePlayerIDs = new();
        List<(int baseID, NeutralZoneController controller)> diedNeutralBases = new();

        // Debug.Log($"[Enqueue:{zoneView.name}] Traitement de {steps.Count} step(s)");
        for (int zeroBasedStepIdx = 0; zeroBasedStepIdx < steps.Count; zeroBasedStepIdx++)
        {
            BattleStepRecord step = steps[zeroBasedStepIdx];
            int stepIdx = zeroBasedStepIdx + 1;
            int attackerHP = GetAttackerCurrentHP(step);
            
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic statsAttackerCreature))
            {
                if (statsAttackerCreature.IsRanged)
                    statsAttackerCreature.owner.matchStats.Add(MatchStatType.RangedUnitAttack);
                else
                    statsAttackerCreature.owner.matchStats.Add(MatchStatType.MeleeUnitAttack);
            }
            switch (step.targetKind)
            {
                case TargetKind.Creature:
                {
                    if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.targetID, out CreatureLogic target)) continue;
                    CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic attackerCreature);
                    Player attackerOwner = attackerCreature?.owner;

                    // Un attaquant déjà IsPendingDeath ici peut avoir deux causes : (1) il est mort lors
                    // d'un step ANTÉRIEUR de cette même liste — son tour ne doit alors plus s'exécuter
                    // (comportement historique) — ou (2) il vient de mourir EN RÉTORSION, pendant la
                    // construction même de CE step : l'OnDeath de sa propre cible, résolu par
                    // anticipation dans AssignSingleAttack/AddPendingCreatureDamage (voir
                    // CreatureLogic.ResolvePredictedBattleDeath), l'a tué avant même que ce
                    // BattleStepRecord ne soit construit. Dans ce second cas, l'attaque a bel et bien
                    // eu lieu — c'est elle qui a déclenché la mort de la cible et donc son OnDeath — et
                    // sauter tout le step empêcherait target.ScheduleBattleDeath() de jamais s'exécuter,
                    // laissant la cible "vivante" pour de bon et son report différé (Command.DeferForBattleReplay)
                    // bloqué indéfiniment. On ne peut pas distinguer les deux cas ici, mais laisser le
                    // step s'exécuter normalement est sans risque dans les deux cas : les mutations de
                    // l'attaquant plus bas (ScheduleBattleDeath/Health) sont déjà protégées par son
                    // propre IsPendingDeath et no-opent proprement s'il est déjà mort.
                    if (target.IsPendingDeath)
                        Debug.LogWarning($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} — cible {target.UniqueCreatureID}({target.DisplayName}) DÉJÀ IsPendingDeath au moment du traitement (tuée par un effet secondaire ?) — contre-attaque fantôme possible");

                    // shieldAbsorbed vient de la planification (voir AddPendingCreatureDamage /
                    // pendingShieldConsumed) — jamais recalculé ici depuis target.ShieldValue "final" :
                    // celui-ci peut avoir continué à grossir après ce coup précis (autres gains réactifs
                    // survenus plus tard dans la même planification), ce qui créditerait rétroactivement
                    // ce coup d'un bouclier qu'il n'avait pas réellement au moment où il a eu lieu.
                    int shieldAbsorbed = step.shieldAbsorbed;
                    int effectiveDamage = step.damage - shieldAbsorbed;
                    int targetHealthAfter = PlannedHealthAfter(target, effectiveDamage, step.targetDies, "cible");
                    Debug.Log($"[Shield/Resolver] {target.DisplayName} — Dégâts bruts: {step.damage} | Shield (au moment du coup): {target.ShieldValue} | Absorbés: {shieldAbsorbed} | Dégâts effectifs: {effectiveDamage} | PV avant: {target.Health} | PV après: {targetHealthAfter}");
                    if (shieldAbsorbed > 0) target.owner.matchStats.Add(MatchStatType.ShieldDamageAbsorbed, shieldAbsorbed);
                    if (effectiveDamage > 0) target.owner.matchStats.Add(MatchStatType.DamageTaken, effectiveDamage);
                    if (effectiveDamage > 0) attackerOwner?.matchStats.Add(MatchStatType.DamageDealt, effectiveDamage);

                    int counterDamage = step.counterDamage;
                    int attackerShieldAbsorbed = step.attackerShieldAbsorbed;
                    int effectiveCounterDamage = counterDamage - attackerShieldAbsorbed;
                    int attackerHealthAfter = attackerCreature != null
                        ? PlannedHealthAfter(attackerCreature, effectiveCounterDamage, step.attackerDies, "attaquant")
                        : Mathf.Max(0, attackerHP - effectiveCounterDamage);
                    Debug.Log($"[Shield/Resolver] {(attackerCreature != null ? attackerCreature.DisplayName : step.attackerID.ToString())} (attaquant) — Contre-dégâts: {counterDamage} | Shield (au moment du coup): {(attackerCreature != null ? attackerCreature.ShieldValue : 0)} | Absorbés: {attackerShieldAbsorbed} | PV avant: {attackerHP} | PV après: {attackerHealthAfter}");
                    if (attackerCreature != null)
                    {
                        if (attackerShieldAbsorbed > 0) attackerCreature.owner.matchStats.Add(MatchStatType.ShieldDamageAbsorbed, attackerShieldAbsorbed);
                        if (effectiveCounterDamage > 0) attackerCreature.owner.matchStats.Add(MatchStatType.DamageTaken, effectiveCounterDamage);
                        if (effectiveCounterDamage > 0) target.owner.matchStats.Add(MatchStatType.DamageDealt, effectiveCounterDamage);
                    }

                    // Cibles secondaires (Cone/Piercing/Circular/...) déjà résolues UNE FOIS pendant la
                    // planification (ResolveAndReserveModifierHits) et transportées telles quelles dans le
                    // step — aucun ciblage n'est recalculé ici, seulement l'application des dégâts (bouclier/
                    // PV après calculés en live, comme pour la cible principale, pour rester exact même si
                    // l'état a bougé depuis la planification). Calculé avant d'empiler la commande principale,
                    // pour que la liste transportée avec elle soit déjà complète — la commande peut s'exécuter
                    // de façon synchrone dès AddToQueue() (file vide + caméra déjà en position), donc la
                    // remplir après coup risquerait qu'elle soit lue vide par la séquence visuelle.
                    // Important : la mutation ci-dessous NE programme PAS la mort des cibles secondaires —
                    // ScheduleBattleDeath() est appelé plus bas, après avoir mis en file la commande d'attaque
                    // principale, pour que la CreatureDieCommand d'une cible secondaire ne s'exécute jamais
                    // avant l'animation qui est censée la tuer.
                    List<AttackHitResult> secondaryHits = new List<AttackHitResult>();
                    if (step.secondaryHits != null)
                        for (int si = 0; si < step.secondaryHits.Count; si++)
                        {
                            AttackHitResult reserved = step.secondaryHits[si];
                            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(reserved.TargetUniqueID, out CreatureLogic secTarget)) continue;
                            if (secTarget.IsPendingDeath) continue; // défensif — ne devrait pas arriver si la résolution planifiée est cohérente
                            // reserved.Absorbed vient de la planification (voir ResolveAndReserveModifierHits) —
                            // même raison que shieldAbsorbed plus haut : jamais recalculé depuis ShieldValue "final".
                            int secShieldAbs = reserved.Absorbed;
                            int secEffective = reserved.Damage - secShieldAbs;
                            bool secDies = step.secondaryDies != null && si < step.secondaryDies.Count && step.secondaryDies[si];
                            int secHealthAfter = PlannedHealthAfter(secTarget, secEffective, secDies, "cible secondaire");
                            secondaryHits.Add(new AttackHitResult(secTarget.UniqueCreatureID, reserved.Damage, secHealthAfter));
                            if (secShieldAbs > 0) secTarget.owner.matchStats.Add(MatchStatType.ShieldDamageAbsorbed, secShieldAbs);
                            if (secEffective > 0) secTarget.owner.matchStats.Add(MatchStatType.DamageTaken, secEffective);
                            if (secEffective > 0) attackerOwner?.matchStats.Add(MatchStatType.DamageDealt, secEffective);
                            if (secHealthAfter > 0)
                            {
                                secTarget.Health = secHealthAfter;
                                secTarget.ConsumeShieldQueued(secShieldAbs);
                            }
                        }

                    // Debug.Log($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} Creature — attaquant={step.attackerID} cible={target.UniqueCreatureID}({target.DisplayName}) dégâts={step.damage} PVcibleAprès={targetHealthAfter} contreDégâts={counterDamage} PVattaquantAprès={attackerHealthAfter}");
                    if (attackerCreature != null && attackerCreature.AttacksLeftThisTurn > 0)
                        attackerCreature.AttacksLeftThisTurn--;
                    CreatureAttackCommand.EnqueueAttack(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter, attackerCreature?.AttackSpeedMultiplier ?? 1f, secondaryHits, step.attackerExhausted);

                    // Issue décidée par la planification (voir PlannedHealthAfter), pas par les PV live.
                    if (targetHealthAfter <= 0)
                        target.ScheduleBattleDeath();
                    else
                    {
                        target.Health = targetHealthAfter;
                        target.ConsumeShieldQueued(shieldAbsorbed);
                    }

                    if (attackerCreature != null)
                    {
                        if (attackerHealthAfter <= 0)
                            attackerCreature.ScheduleBattleDeath();
                        else
                        {
                            attackerCreature.Health = attackerHealthAfter;
                            attackerCreature.ConsumeShieldQueued(attackerShieldAbsorbed);
                        }
                    }

                    // Mort des cibles secondaires tuées par un modificateur — mise en file seulement maintenant,
                    // après la CreatureAttackCommand principale (voir commentaire plus haut).
                    foreach (AttackHitResult hit in secondaryHits)
                    {
                        if (hit.HealthAfter <= 0 && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(hit.TargetUniqueID, out CreatureLogic secondaryCreature))
                            secondaryCreature.ScheduleBattleDeath();
                    }

                    // Révèle les effets OnTakeDamage de ce step, maintenant que le coup entier (cible
                    // principale + contre-dégâts + touchés secondaires) a été mis en file visuellement —
                    // même principe que le flush OnAttack de CreatureAttackCommand.EnqueueAttack, mais
                    // une clé PAR COUP plutôt que par créature (voir OnTakeDamageDeferKey).
                    Command.FlushDeferredCommands(OnTakeDamageDeferKey(zeroBasedStepIdx, 0));
                    if (counterDamage > 0)
                        Command.FlushDeferredCommands(OnTakeDamageDeferKey(zeroBasedStepIdx, 1));
                    // Indexé sur step.secondaryHits (la liste figée en planification), PAS sur la liste
                    // locale secondaryHits reconstruite juste au-dessus : cette dernière peut sauter des
                    // entrées (cible introuvable / déjà IsPendingDeath), ce qui décalerait les index et
                    // flusherait la MAUVAISE clé — laissant l'effet OnTakeDamage du vrai touché
                    // correspondant bloqué pour toujours dans Command._deferredBySource.
                    if (step.secondaryHits != null)
                        for (int si = 0; si < step.secondaryHits.Count; si++)
                            Command.FlushDeferredCommands(OnTakeDamageDeferKey(zeroBasedStepIdx, 2 + si));

                    break;
                }
                case TargetKind.Base:
                {
                    if (!BaseLogic.BasesCreatedThisGame.TryGetValue(step.targetID, out BaseLogic target))
                    {
                        // Debug.LogWarning($"[Enqueue:{zoneView.name}][EnqueueBase] step {stepIdx}/{steps.Count} — Base introuvable id={step.targetID} — step ignoré !");
                        continue;
                    }
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
                    // Debug.Log($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} Base — attaquant={step.attackerID} cible base={target.ID}({target.DisplayName}) dégâts={step.damage} PVaprès={targetHealthAfter}");
                    CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic baseAttackerCreature);
                    if (baseAttackerCreature != null && baseAttackerCreature.AttacksLeftThisTurn > 0)
                        baseAttackerCreature.AttacksLeftThisTurn--;
                    CreatureAttackCommand.EnqueueAttack(step.targetID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter, 1f, null, step.attackerExhausted);
                    if (target.IsHomeBase || targetHealthAfter > 0)
                        target.Health = targetHealthAfter;
                    else
                    {
                        // Capturé AVANT Die() : Die() retire cette base de BasesCreatedThisGame, donc
                        // plus rien ne permettrait de la retrouver après coup.
                        diedNeutralBases.Add((target.ID, target.neutralBaseController));
                        target.Die();
                    }

                    break;
                }
                case TargetKind.Player:
                {
                    Player target = step.targetOwnerPlayerID == GlobalSettings.Instance.LowPlayer.PlayerID
                        ? GlobalSettings.Instance.LowPlayer
                        : GlobalSettings.Instance.TopPlayer;
                    // Pas de plancher à 0 ici, contrairement aux autres TargetKind : le PV final de la
                    // base principale peut être négatif (overkill) — ComputeRoundOutcome() en a besoin
                    // pour départager le scénario "les deux bases meurent" (voir la conversation de
                    // conception), et le joueur doit pouvoir le voir affiché tel quel.
                    int targetHealthAfter = target.Health - step.damage;
                    Debug.Log($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} Player — attaquant={step.attackerID} cible joueur={target.name} HP avant={target.Health} dégâts={step.damage} → HP après={targetHealthAfter}");
                    CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic playerAttackerCreature);
                    if (playerAttackerCreature != null && playerAttackerCreature.AttacksLeftThisTurn > 0)
                        playerAttackerCreature.AttacksLeftThisTurn--;
                    CreatureAttackCommand.EnqueueAttack(target.PlayerID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter, 1f, null, step.attackerExhausted);
                    target.Health = targetHealthAfter;
                    if (targetHealthAfter <= 0)
                        diedHomeBasePlayerIDs.Add(target.PlayerID); // HashSet : dédoublonne les coups fatals répétés
                    break;
                }
            }
            Command.FlushPendingDeaths();
        }

        // Animations de mort des bases — une seule fois chacune, ici, après que toute la zone ait fini
        // d'être traitée (voir commentaire au-dessus de la boucle). Bloque naturellement la suite de la
        // file (zone suivante / GameOverCommand déjà enfilé juste après par l'appelant) puisque ces
        // Command ne rappellent CommandExecutionComplete qu'une fois l'animation terminée.
        foreach ((int baseID, NeutralZoneController controller) in diedNeutralBases)
            new BaseDieCommand(baseID, controller).AddToQueue();
        // Le bâtiment cesse aussitôt d'être une base (voir Player.DestroyHomeBuilding) — ComputeRoundOutcome
        // a déjà tranché ce round : s'il était la dernière base, GameOverCommand suit ; sinon la partie
        // continue sans lui.
        foreach (int deadPlayerID in diedHomeBasePlayerIDs)
        {
            new MainBaseDeathAnimationCommand(deadPlayerID).AddToQueue();
            Player deadPlayer = deadPlayerID == GlobalSettings.Instance.LowPlayer.PlayerID
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
            deadPlayer.DestroyHomeBuilding();
        }

        Debug.Log($"[Enqueue:{zoneView.name}] Terminé — {steps.Count} step(s) traité(s), file de commandes: {Command.CommandQueue.Count} en attente, playingQueue={Command.playingQueue}");
    }

    public void EnqueueZoneClashMove()
    {
        bool anyCombat = pendingDamage.Count > 0 || pendingBaseDamage.Count > 0
                      || pendingPlayerDamage.Count > 0;
        List<(int creatureID, Vector3 targetPos)> moves = new();
        List<CreatureLogic> allCreatures = new();
        allCreatures.AddRange(GetCreaturesInMyZone(GlobalSettings.Instance.LowPlayer, zoneView));
        allCreatures.AddRange(GetCreaturesInMyZone(GlobalSettings.Instance.TopPlayer, zoneView));
        foreach (CreatureLogic creature in allCreatures)
        {
            PlayerArea area = FindAreaForCreature(creature);
            if (area?.BattlePos != null)
                moves.Add((creature.UniqueCreatureID, area.BattlePos.position));
        }
        if (moves.Count > 0 && anyCombat)
            new ZoneClashMoveCommand(moves, 0.2f).AddToQueue();
    }

    // Scopé à resolverIdxs plutôt qu'à allResolvers : au moment de broadcaster une étape, les
    // resolvers des étapes suivantes n'ont pas encore été (re)planifiés ce round-ci et traînent
    // encore _lastBattleSteps d'un round précédent — les inclure ferait fuiter ces données périmées
    // dans le broadcast de l'étape courante.
    public static void SerializeBattleStepsForResolvers(
        List<int> resolverIdxs,
        out int[] resolverIdxsOut, out int[] attackerIDs,
        out int[] targetIDs, out int[] targetKinds, out int[] damages, out int[] ownerPlayerIDs,
        out int[] secondaryCounts, out int[] secondaryTargetIDs, out int[] secondaryDamages,
        out int[] counterDamages, out int[] attackerExhausted,
        out int[] shieldAbsorbed, out int[] attackerShieldAbsorbed, out int[] secondaryAbsorbed,
        out int[] targetDies, out int[] attackerDies, out int[] secondaryDies)
    {
        List<int> ri = new(); List<int> ai = new();
        List<int> ti = new();
        List<int> tk = new(); List<int> dg = new();
        List<int> op = new();
        List<int> scnt = new(); List<int> stid = new(); List<int> sdmg = new(); List<int> sabs = new();
        List<int> cd = new();
        List<int> ex = new();
        List<int> sha = new(); List<int> asha = new();
        List<int> tdie = new(); List<int> adie = new(); List<int> sdie = new();

        foreach (int i in resolverIdxs)
        {
            if (allResolvers[i]._lastBattleSteps == null) continue;
            foreach (BattleStepRecord s in allResolvers[i]._lastBattleSteps)
            {
                ri.Add(i);  ai.Add(s.attackerID);
                ti.Add(s.targetID);
                tk.Add((int)s.targetKind);
                dg.Add(s.damage);
                op.Add(s.targetOwnerPlayerID);
                cd.Add(s.counterDamage);
                ex.Add(s.attackerExhausted ? 1 : 0);
                sha.Add(s.shieldAbsorbed);
                asha.Add(s.attackerShieldAbsorbed);
                tdie.Add(s.targetDies ? 1 : 0);
                adie.Add(s.attackerDies ? 1 : 0);

                int secCount = s.secondaryHits?.Count ?? 0;
                scnt.Add(secCount);
                for (int k = 0; k < secCount; k++)
                {
                    AttackHitResult hit = s.secondaryHits[k];
                    stid.Add(hit.TargetUniqueID);
                    sdmg.Add(hit.Damage);
                    sabs.Add(hit.Absorbed);
                    sdie.Add(s.secondaryDies != null && k < s.secondaryDies.Count && s.secondaryDies[k] ? 1 : 0);
                }
            }
        }
        resolverIdxsOut = ri.ToArray(); attackerIDs    = ai.ToArray();
        targetIDs      = ti.ToArray();
        targetKinds    = tk.ToArray(); damages        = dg.ToArray();
        ownerPlayerIDs = op.ToArray();
        secondaryCounts    = scnt.ToArray();
        secondaryTargetIDs = stid.ToArray();
        secondaryDamages   = sdmg.ToArray();
        counterDamages     = cd.ToArray();
        attackerExhausted  = ex.ToArray();
        shieldAbsorbed         = sha.ToArray();
        attackerShieldAbsorbed = asha.ToArray();
        secondaryAbsorbed      = sabs.ToArray();
        targetDies    = tdie.ToArray();
        attackerDies  = adie.ToArray();
        secondaryDies = sdie.ToArray();
    }

    // La liste des resolvers concernés par cette étape n'est PAS déduite des tableaux à plat
    // (resolverIdxs ne contient que les resolvers ayant produit au moins un step — un resolver sans
    // combat n'y apparaît pas du tout) : le serveur la transmet explicitement (stageResolverOrder,
    // déjà dans l'ordre de rejeu — RoundOutcome.MainBaseOrder pour la Base principale). Jamais
    // recalculée ici via BuildBattleStagePlan : la répartition dépend de la position des unités-bases,
    // qui a pu changer depuis que le serveur l'a figée en début de Battle Phase. Ça garantit aussi
    // qu'un resolver sans combat cette étape reçoit quand même EnqueueBattleCommands([]) (nécessaire
    // pour purger ses popups OnBattleStart différés via zoneDeferKey), comme le fait déjà le serveur.
    public static void EnqueueStageReconstructedBattleCommands(
        int[] resolverIdxs, int[] attackerIDs,
        int[] targetIDs, int[] targetKinds, int[] damages, int[] ownerPlayerIDs,
        int[] secondaryCounts, int[] secondaryTargetIDs, int[] secondaryDamages,
        int[] counterDamages, int[] attackerExhausted,
        int[] shieldAbsorbed, int[] attackerShieldAbsorbed, int[] secondaryAbsorbed,
        int[] targetDies, int[] attackerDies, int[] secondaryDies,
        BattleStage stage, bool decisive, bool isDraw, int winnerPlayerID,
        int[] stageResolverOrder)
    {
        Dictionary<int, List<BattleStepRecord>> stepsByResolver = new();
        int secCursor = 0;
        for (int i = 0; i < resolverIdxs.Length; i++)
        {
            int rIdx = resolverIdxs[i];
            if (!stepsByResolver.ContainsKey(rIdx))
                stepsByResolver[rIdx] = new List<BattleStepRecord>();

            List<AttackHitResult> secondaryHits = new List<AttackHitResult>();
            List<bool> stepSecondaryDies = new List<bool>();
            int secCount = (secondaryCounts != null && i < secondaryCounts.Length) ? secondaryCounts[i] : 0;
            for (int k = 0; k < secCount; k++)
            {
                int secAbs = (secondaryAbsorbed != null && secCursor < secondaryAbsorbed.Length) ? secondaryAbsorbed[secCursor] : 0;
                secondaryHits.Add(new AttackHitResult(secondaryTargetIDs[secCursor], secondaryDamages[secCursor], 0, secAbs));
                stepSecondaryDies.Add(secondaryDies != null && secCursor < secondaryDies.Length && secondaryDies[secCursor] != 0);
                secCursor++;
            }

            stepsByResolver[rIdx].Add(new BattleStepRecord
            {
                attackerID          = attackerIDs[i],
                targetID            = targetIDs[i],
                targetKind          = (TargetKind)targetKinds[i],
                damage              = damages[i],
                targetOwnerPlayerID = ownerPlayerIDs[i],
                secondaryHits       = secondaryHits,
                counterDamage       = (counterDamages != null && i < counterDamages.Length) ? counterDamages[i] : 0,
                attackerExhausted   = (attackerExhausted != null && i < attackerExhausted.Length) && attackerExhausted[i] != 0,
                shieldAbsorbed         = (shieldAbsorbed != null && i < shieldAbsorbed.Length) ? shieldAbsorbed[i] : 0,
                attackerShieldAbsorbed = (attackerShieldAbsorbed != null && i < attackerShieldAbsorbed.Length) ? attackerShieldAbsorbed[i] : 0,
                targetDies    = targetDies != null && i < targetDies.Length && targetDies[i] != 0,
                attackerDies  = attackerDies != null && i < attackerDies.Length && attackerDies[i] != 0,
                secondaryDies = stepSecondaryDies
            });
        }

        System.Func<int, List<BattleStepRecord>> resolveSteps = idx =>
            stepsByResolver.TryGetValue(idx, out List<BattleStepRecord> found) ? found : new List<BattleStepRecord>();

        List<int> stageIdxs = new List<int>(stageResolverOrder ?? System.Array.Empty<int>());
        if (stage == BattleStage.MainBase)
        {
            RoundOutcome outcome = new RoundOutcome
            {
                Decisive = decisive,
                IsDraw = isDraw,
                WinnerPlayerID = winnerPlayerID,
                MainBaseOrder = stageIdxs
            };
            EnqueueMainBaseBattleCommands(outcome, resolveSteps);
        }
        else
        {
            EnqueueStageBattleCommands(stageIdxs, resolveSteps);
        }
    }

    // Les attaquants non-mêlée (ranged) ne subissent pas les dégâts de contre-attaque de leur cible
    bool IsMeleeAttacker(int attackerID)
    {
        return CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic c) && c.IsMelee;
    }

    int GetAttackerCurrentHP(BattleStepRecord step)
    {
        return CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic c) ? c.Health : 0;
    }

    // Une unité qui a reçu des dégâts létaux en séquence ne peut plus attaquer
    bool IsAttackerDead((int attack, int id) attacker)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attacker.id, out CreatureLogic c)) return true;
        return IsEffectivelyDead(c);
    }

    // Valeur d'attaque actuelle (live) d'un attaquant identifié par ID — reflète un éventuel
    // buff OnDeath déjà résolu plus tôt dans cette même planification, moins l'attaque d'aura dont la
    // perte est déjà anticipée (voir ReservePredictedAuraLoss). fallbackValue est utilisé si l'entité
    // est introuvable (ne devrait pas arriver, garde défensive).
    int GetLiveAttack(int id, int fallbackValue)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic c)) return fallbackValue;
        pendingAttackLoss.TryGetValue(id, out int attackLoss);
        return Mathf.Max(0, c.Attack - attackLoss);
    }

    // La source d'une aura de stats vient d'être tuée en planification. Au rejeu, ses cibles de cette
    // zone perdront l'aura à l'instant de sa mort réelle (CreatureLogic.MarkPendingDeath →
    // PassiveAuraManager.Recompute), PV actuels plafonnés au nouveau max (EffectSO.ShiftStatsCapped).
    // Les stats live ne bougent pas encore — les coups déjà planifiés doivent être rejoués sur les PV
    // buffés — donc on anticipe ici la même perte : PV perdus ajoutés à pendingDamage, attaque perdue
    // retirée de GetLiveAttack pour les coups et ripostes planifiés ensuite.
    void ReservePredictedAuraLoss(CreatureLogic source)
    {
        foreach ((CreatureLogic target, int attackLoss, int healthLoss) in PassiveAuraManager.GetStatAuraLossOnDeath(source))
        {
            if (IsEffectivelyDead(target)) continue;
            int id = target.UniqueCreatureID;

            if (attackLoss != 0)
            {
                pendingAttackLoss.TryGetValue(id, out int existingAttackLoss);
                pendingAttackLoss[id] = existingAttackLoss + attackLoss;
            }
            if (healthLoss == 0) continue;

            pendingDamage.TryGetValue(id, out int existingDamage);
            pendingMaxHealthLoss.TryGetValue(id, out int existingMaxLoss);
            int remaining = target.Health - existingDamage;
            int newMax = target.MaxHealth - existingMaxLoss - healthLoss;
            pendingMaxHealthLoss[id] = existingMaxLoss + healthLoss;
            if (remaining > newMax)
                pendingDamage[id] = existingDamage + (remaining - newMax);
        }
    }

    // Clé de report par COUP (pas par créature) pour OnTakeDamage — contrairement à OnDeath/OnAttack (au
    // plus une fois par créature et par bataille), une créature peut subir plusieurs coups dans la MÊME
    // bataille (contre-attaques, dégâts de zone, ciblage répété) : chaque coup doit pouvoir être révélé
    // séparément, à son propre instant visuel. Dérivée de (resolver, position du step dans la séquence
    // planifiée, rôle du coup au sein de ce step) — calculée à l'IDENTIQUE en planification
    // (AddPendingCreatureDamage, ci-dessous) et en enqueue (EnqueueBattleCommands), sans transiter par le
    // réseau : seule la VALEUR finale l'est (voir PredictedTriggerReplay.DeferKey), exactement comme un
    // CardEffectData déjà "figé" l'est pour OnDeath/OnAttack.
    // subIndex : 0 = cible principale, 1 = contre-dégâts reçus par l'attaquant, 2+n = n-ième touché
    // secondaire (AttackModifierSO). Décalage choisi pour ne collisionner ni avec OnAttackDeferKeyBase
    // (-1 500 000 000, CreatureLogic), ni avec ZoneDeferKeyBase (-2 000 000 000), ni avec les IDs réels/locaux.
    private const int OnTakeDamageDeferKeyBase = -1_700_000_000;
    private int OnTakeDamageDeferKey(int stepIndex, int subIndex) =>
        OnTakeDamageDeferKeyBase - (resolverIndex * 1_000_000) - (stepIndex * 1000) - subIndex;

    // Ajoute `amount` (dégât BRUT) aux dégâts prédits (pendingDamage, désormais NET de bouclier — voir
    // plus bas) de la créature `creatureID`, en répartissant ce coup précis en absorbé/net à l'aide du
    // bouclier RESTANT à cet instant (pendingShieldConsumed) — jamais avec le ShieldValue "final" une
    // fois toute la planification terminée. Retourne le montant absorbé par CE coup, à conserver dans
    // le BattleStepRecord/AttackHitResult correspondant : EnqueueBattleCommands ne doit plus JAMAIS
    // recalculer cette absorption depuis ShieldValue à l'exécution (voir commentaire sur
    // pendingShieldConsumed), seulement rejouer ce chiffre déjà figé, pour rester cohérent avec la
    // décision de mort/éligibilité prise ici.
    //
    // Si cette mise à jour fait franchir à `creature` le seuil de mort prédite pour la première fois,
    // résout son OnDeath immédiatement (voir CreatureLogic.ResolvePredictedBattleDeath) — avant
    // qu'aucune attaque suivante de la séquence ne soit assignée, pour que le buff éventuel s'applique
    // aux dégâts et à la survie du reste du combat.
    int AddPendingCreatureDamage(int creatureID, int amount, int stepIndex, int subIndex)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureID, out CreatureLogic creature))
        {
            pendingDamage.TryGetValue(creatureID, out int existingFallback);
            pendingDamage[creatureID] = existingFallback + amount;
            return 0;
        }

        bool wasAliveBefore = !IsEffectivelyDead(creature);

        // Absorption de CE coup, calculée AVANT que son propre gain réactif (ci-dessous) ne puisse la
        // protéger elle-même — même philosophie que OnDeath/OnAttack, qui n'affectent jamais
        // l'évènement qui les a déclenchés.
        pendingShieldConsumed.TryGetValue(creatureID, out int shieldAlreadyConsumed);
        int shieldRemaining = Mathf.Max(0, creature.ShieldValue - shieldAlreadyConsumed);
        int absorbedNow = Mathf.Min(amount, shieldRemaining);
        int netAmount = amount - absorbedNow;
        if (absorbedNow > 0)
            pendingShieldConsumed[creatureID] = shieldAlreadyConsumed + absorbedNow;

        pendingDamage.TryGetValue(creatureID, out int existing);
        pendingDamage[creatureID] = existing + netAmount;

        // OnTakeDamage : quel que soit le montant, et même si CE coup tue la créature (contrairement au
        // bloc OnDeath juste en dessous, aucune condition ici sur wasAliveBefore/IsEffectivelyDead).
        if (amount > 0)
            creature.ResolvePredictedOnTakeDamage(OnTakeDamageDeferKey(stepIndex, subIndex));

        if (wasAliveBefore && IsEffectivelyDead(creature) && !creature.OnDeathResolvedInBattle)
        {
            Debug.Log($"[OnDeath:{zoneView.name}] Mort prédite en planification — {creature.DisplayName}(ID:{creature.UniqueCreatureID}) — résolution immédiate de OnDeath");
            creature.ResolvePredictedBattleDeath();
            ReservePredictedAuraLoss(creature);
        }

        return absorbedNow;
    }

    // IsPendingDeath couvre les morts de BattleStart ; pendingDamage >= Health couvre les morts en séquence de Battle
    // pendingDamage est désormais déjà NET (bouclier déduit coup par coup au moment de chaque coup,
    // voir AddPendingCreatureDamage) — ne PAS re-soustraire c.ShieldValue ici : ce serait relire un
    // bouclier potentiellement déjà gonflé par des gains réactifs survenus APRÈS les coups comptés
    // dans d, et créditer ces gains rétroactivement à des coups qu'ils n'ont jamais protégés.
    bool IsEffectivelyDead(CreatureLogic c)
    {
        if (c.IsPendingDeath) return true;
        if (!pendingDamage.TryGetValue(c.UniqueCreatureID, out int d)) return false;
        return d >= c.Health;
    }

    List<CreatureLogic> GetCreaturesInMyZone(Player player, ZoneManager zone)
    {
        List<CreatureLogic> result = new();
        foreach (PlayerArea pa in zone.subZones)
        {
            if (pa.owner == GetAreaPosition(player))
            {
                foreach (CreatureLogic c in player.playedCards.Creatures)
                {
                    if (c.BaseID != pa.baseID) continue;
                    if (c.IsBoarded)
                    {
                        //Debug.Log($"[Transport] GetCreaturesInMyZone — excluding boarded {c.DisplayName}(ID:{c.UniqueCreatureID}) from combat in zone {zone.name}");
                        continue;
                    }
                    result.Add(c);
                }
            }
        }
        return result;
    }

    void ClearAllIndicators()
    {
        p1FreePool = 0;
        p2FreePool = 0;
    }

    AreaPosition GetAreaPosition(Player player)
    {
        return player == GlobalSettings.Instance.LowPlayer ? AreaPosition.Low : AreaPosition.Top;
    }

    public static bool WouldSurvive(CreatureLogic creature) => PredictedHealth(creature) > 0;

    // Santé prédite de cette créature à l'issue de ce round (dégâts en attente moins bouclier,
    // jamais sous 0) — version numérique de WouldSurvive, nécessaire pour ComputeRoundOutcome quand
    // un joueur a des unités-bases (voir Player.HomeUnits) : contrairement à Player.Health, une
    // simple comparaison à 0 ne suffit pas, il faut aussi comparer les PV finaux des deux joueurs
    // entre eux pour décider qui joue en premier.
    public static int PredictedHealth(CreatureLogic creature)
    {
        // IsPendingDeath ne couvre que la mort EN combat (MarkPendingDeath) — une créature tuée hors
        // combat (ex: Die() direct, voir Assimilate) ne le pose jamais. Sans le check Health <= 0,
        // une créature déjà morte mais restée fautivement dans playedCards.Creatures (voir
        // Player.ResyncCreatureOrderForArea) compterait comme occupant une place de rangée.
        if (creature.IsPendingDeath || creature.Health <= 0)
            return 0;
        foreach (ZoneCombatResolver r in allResolvers)
            // pendingDamage est déjà net de bouclier (voir AddPendingCreatureDamage) — ne pas
            // re-soustraire creature.ShieldValue ici, sous peine de déduire deux fois le bouclier.
            if (r.pendingDamage.TryGetValue(creature.UniqueCreatureID, out int dmg))
                return Mathf.Max(0, creature.Health - dmg);
        return creature.Health; // no pending damage → unchanged
    }
    public int GetRemainingPool(AreaPosition attackerSide)
    {
        return attackerSide == AreaPosition.Low ? p1FreePool : p2FreePool;
    }
    BaseLogic FindDefenderBaseInZone(Player defender)
    {
        foreach (BaseLogic _base in BaseLogic.BasesCreatedThisGame.Values)
        {
            if (_base.owner != defender) continue;
            if (_base.neutralBaseController != null && _base.neutralBaseController.zone == zoneView)
                return _base;
        }
        return null;
    }

    // Une invasion = un combat (au moins un step) dans une zone où l'adversaire a une base, auquel le
    // joueur participe (attaquant d'un step, ou créature ciblée). Comptée une seule fois par zone et par
    // round. Appelé depuis EnqueueBattleCommands et non à la planification : ce code tourne des deux
    // côtés en réseau (steps identiques), la planification uniquement sur le serveur.
    void RecordInvasions(List<BattleStepRecord> steps)
    {
        if (steps.Count == 0) return;

        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;
        bool p1Engaged = false;
        bool p2Engaged = false;
        foreach (BattleStepRecord step in steps)
        {
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic attacker))
            {
                if (attacker.owner == p1) p1Engaged = true;
                else if (attacker.owner == p2) p2Engaged = true;
            }
            if (step.targetKind == TargetKind.Creature)
            {
                if (step.targetOwnerPlayerID == p1.PlayerID) p1Engaged = true;
                else if (step.targetOwnerPlayerID == p2.PlayerID) p2Engaged = true;
            }
        }

        if (p1Engaged && HasBaseInThisZone(p2)) p1.matchStats.Add(MatchStatType.Invasions);
        if (p2Engaged && HasBaseInThisZone(p1)) p2.matchStats.Add(MatchStatType.Invasions);
    }

    // Base principale (bâtiment, ou chaque unité-base — voir Player.HomeUnits) ou base neutre capturée.
    bool HasBaseInThisZone(Player player)
    {
        foreach (BaseLogic b in player.controlledBases)
            if (b.Zone == zoneView.Logic) return true;
        foreach (CreatureLogic unit in player.HomeUnits)
            if (OwnsCreature(unit.BaseID)) return true;
        return false;
    }

    // Called from OneCreatureManager click — finds which resolver owns a baseID
    public static ZoneCombatResolver FindForBase(int baseID)
    {
        foreach (ZoneCombatResolver r in allResolvers)
            if (r.OwnsCreature(baseID)) return r;
        return null;
    }

    public bool OwnsCreature(int baseID)
    {
        foreach (PlayerArea pa in zoneView.subZones)
            if (pa.baseID == baseID) return true;
        return false;
    }

    Player GetOwnerPlayer(CreatureLogic creature)
    {
        foreach (PlayerArea pa in zoneView.subZones)
        {
            if (pa.baseID != creature.BaseID) continue;
            return pa.owner == AreaPosition.Low
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
        }
        return null;
    }


    // -------------------------------------------------------------------------
    // SYNCHRONISATION RÉSEAU — ATTRIBUTION DES DÉGÂTS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Regroupe les attributions de dégâts d'un joueur sous forme de tableaux sérialisables,
    /// prêts à être envoyés au serveur via un RPC.
    /// Chaque joueur ne sérialise QUE les dégâts qu'il contrôle (ses propres attaques
    /// ciblant les entités ennemies), car l'autre joueur gère son propre pool d'attaque.
    /// </summary>
    public struct BattleAssignment
    {
        public int[] CreatureIDs;
        public int[] CreatureDamages;
        public int[] BaseIDs;
        public int[] BaseDamages;
        public int[] TargetPlayerIDs;
        public int[] PlayerDamages;
        public int[] ResolverP1Pools;
        public int[] ResolverP2Pools;
    }

    public static BattleAssignment SerializeMyAttackAssignments(int attackerPlayerIndex)
    {
        Player attacker = Player.Players[attackerPlayerIndex];

        // On cherche l'ennemi : le joueur qui reçoit les attaques de 'attacker'
        Player enemy;
        if (attacker == GlobalSettings.Instance.LowPlayer)
            enemy = GlobalSettings.Instance.TopPlayer;
        else
            enemy = GlobalSettings.Instance.LowPlayer;

        List<int> creatureIDList    = new List<int>();
        List<int> creatureDmgList   = new List<int>();
        List<int> baseIDList        = new List<int>();
        List<int> baseDmgList       = new List<int>();
        List<int> playerIDList      = new List<int>();
        List<int> playerDmgList     = new List<int>();

        foreach (ZoneCombatResolver resolver in allResolvers)
        {
            foreach (KeyValuePair<int, int> entry in resolver.pendingDamage)
            {
                if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(entry.Key, out CreatureLogic creature)) continue;
                if (resolver.GetOwnerPlayer(creature) != enemy) continue;
                creatureIDList.Add(entry.Key);
                creatureDmgList.Add(entry.Value);
            }

            foreach (KeyValuePair<int, int> entry in resolver.pendingBaseDamage)
            {
                if (!BaseLogic.BasesCreatedThisGame.TryGetValue(entry.Key, out BaseLogic _base)) continue;
                if (_base.owner != enemy) continue;
                baseIDList.Add(entry.Key);
                baseDmgList.Add(entry.Value);
            }

            if (resolver.pendingPlayerDamage.TryGetValue(enemy.PlayerID, out int pendingPlayerDmg))
            {
                playerIDList.Add(enemy.PlayerID);
                playerDmgList.Add(pendingPlayerDmg);
            }
        }

        return new BattleAssignment
        {
            CreatureIDs     = creatureIDList.ToArray(),
            CreatureDamages = creatureDmgList.ToArray(),
            BaseIDs     = baseIDList.ToArray(),
            BaseDamages = baseDmgList.ToArray(),
            TargetPlayerIDs = playerIDList.ToArray(),
            PlayerDamages   = playerDmgList.ToArray()
        };
    }

    /// <summary>
    /// Sérialise l'état de combat calculé par le serveur, limité aux resolvers de l'étape en cours.
    /// Scopé plutôt que sur allResolvers pour la même raison que SerializeBattleStepsForResolvers :
    /// les resolvers des étapes suivantes n'ont pas encore été (re)planifiés ce round-ci.
    /// Appelé côté serveur après BuildAutoBattleSequence() pour broadcaster l'état canonique.
    /// </summary>
    public static BattleAssignment SerializeAssignmentsForResolvers(List<int> resolverIdxs)
    {
        List<int> cIDs  = new(); List<int> cDmgs  = new();
        List<int> bIDs  = new(); List<int> bDmgs  = new();
        List<int> pIDs  = new(); List<int> pDmgs  = new();

        foreach (int idx in resolverIdxs)
        {
            ZoneCombatResolver r = allResolvers[idx];
            foreach (KeyValuePair<int, int> kvp in r.pendingDamage)        { cIDs.Add(kvp.Key);  cDmgs.Add(kvp.Value);  }
            foreach (KeyValuePair<int, int> kvp in r.pendingBaseDamage)    { bIDs.Add(kvp.Key);  bDmgs.Add(kvp.Value);  }
            foreach (KeyValuePair<int, int> kvp in r.pendingPlayerDamage)  { pIDs.Add(kvp.Key);  pDmgs.Add(kvp.Value);  }
        }

        // Les pools d'overflow (p1FreePool/p2FreePool) restent des tableaux DENSES de taille
        // allResolvers.Count (0 hors resolverIdxs) — forme du fil inchangée, ApplyCanonicalPools n'a
        // besoin d'aucune modification.
        int[] p1Pools = new int[allResolvers.Count];
        int[] p2Pools = new int[allResolvers.Count];
        foreach (int idx in resolverIdxs)
        {
            p1Pools[idx] = allResolvers[idx].p1FreePool;
            p2Pools[idx] = allResolvers[idx].p2FreePool;
        }

        return new BattleAssignment
        {
            CreatureIDs     = cIDs.ToArray(),  CreatureDamages = cDmgs.ToArray(),
            BaseIDs         = bIDs.ToArray(),  BaseDamages     = bDmgs.ToArray(),
            TargetPlayerIDs = pIDs.ToArray(),  PlayerDamages   = pDmgs.ToArray(),
            ResolverP1Pools = p1Pools,
            ResolverP2Pools = p2Pools
        };
    }

    /// <summary>
    /// Applique les valeurs d'overflow (freePool) reçues du serveur sur chaque resolver local.
    /// Appelé après ApplyCanonicalAssignment() pour synchroniser l'affichage UI de l'overflow.
    /// </summary>
    public static void ApplyCanonicalPools(int[] p1Pools, int[] p2Pools)
    {
        for (int i = 0; i < allResolvers.Count && i < p1Pools.Length; i++)
        {
            allResolvers[i].p1FreePool = p1Pools[i];
            allResolvers[i].p2FreePool = p2Pools[i];
        }
    }

    /// <summary>
    /// Applique l'attribution canonique envoyée par le serveur, en remplacement
    /// complet des dictionnaires locaux de tous les resolvers.
    /// Doit être appelé avant OnBattlePhaseEnd() pour garantir que les dégâts
    /// appliqués sont identiques sur tous les clients.
    /// </summary>
    public static void ApplyCanonicalAssignment(
        int[] creatureIDs,     int[] creatureDamages,
        int[] baseIDs,         int[] baseDamages,
        int[] targetPlayerIDs, int[] playerDamages)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
        {
            resolver.pendingDamage.Clear();
            resolver.pendingBaseDamage.Clear();
            resolver.pendingPlayerDamage.Clear();
        }

        for (int i = 0; i < creatureIDs.Length; i++)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureIDs[i], out CreatureLogic creature)) continue;
            ZoneCombatResolver ownerResolver = FindForBase(creature.BaseID);
            if (ownerResolver != null) ownerResolver.pendingDamage[creatureIDs[i]] = creatureDamages[i];
        }

        for (int i = 0; i < baseIDs.Length; i++)
        {
            if (!BaseLogic.BasesCreatedThisGame.TryGetValue(baseIDs[i], out BaseLogic _base)) continue;
            ZoneCombatResolver ownerResolver = FindResolverForBase(_base);
            if (ownerResolver != null) ownerResolver.pendingBaseDamage[baseIDs[i]] = baseDamages[i];
        }

        for (int i = 0; i < targetPlayerIDs.Length; i++)
        {
            Player targetPlayer = targetPlayerIDs[i] == GlobalSettings.Instance.LowPlayer.PlayerID
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
            ZoneCombatResolver ownerResolver = FindResolverForPlayer(targetPlayer);
            if (ownerResolver != null) ownerResolver.pendingPlayerDamage[targetPlayerIDs[i]] = playerDamages[i];
        }
    }

    static ZoneCombatResolver FindResolverForBase(BaseLogic _base)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
            if (_base.neutralBaseController?.zone == resolver.zoneView) return resolver;
        return null;
    }

    static ZoneCombatResolver FindResolverForPlayer(Player player)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
            if (resolver.zoneView.subZones.Contains(player.MainPArea)) return resolver;
        return null;
    }

    // -------------------------------------------------------------------------
    // ISSUE DE PARTIE — ORDRE DES COMBATS DE BASE PRINCIPALE, VICTOIRE/DÉFAITE
    // -------------------------------------------------------------------------

    public struct RoundOutcome
    {
        public bool Decisive;
        public bool IsDraw;
        public int WinnerPlayerID;          // -1 si égalité ou round non décisif
        public List<int> MainBaseOrder;     // resolvers de l'étape Base principale (voir
                                            // BattleStagePlan.MainBaseResolverIdxs), dans l'ordre de rejeu
    }

    // Calculé une fois par round, une fois que les resolvers de l'étape Base principale ont fini
    // leur planification (BuildAutoBattleSequence, voir BattleStage.MainBase) mais AVANT qu'aucune
    // commande de cette étape ne soit enfilée — le serveur (ou la machine solo) connaît donc déjà
    // l'issue de la partie avant la moindre animation de Base principale (les étapes Rencontres
    // précédentes, elles, ont déjà été enfilées et animées — voir TurnManager.DelayedBattleStart).
    // Un joueur perd quand il ne lui reste plus aucune base (voir PredictRemainingBases).
    //
    // mainBaseResolverIdxs = BattleStagePlan.MainBaseResolverIdxs du plan figé en début de Battle
    // Phase : MainBaseOrder n'en est qu'une permutation, jamais une liste recalculée ici — une zone
    // déjà jouée en Rencontres ne peut donc jamais être rejouée, ni une zone planifiée pour cette
    // étape sautée, même si une unité-base est morte ou a été déplacée entre-temps. Les zones où le
    // joueur en retard (score le plus bas) a une base passent en dernier : le combat décisif est ainsi
    // le dernier animé avant GameOverCommand (voir EnqueueMainBaseBattleCommands).
    public static RoundOutcome ComputeRoundOutcome(List<int> mainBaseResolverIdxs)
    {
        Player low = GlobalSettings.Instance.LowPlayer;
        Player top = GlobalSettings.Instance.TopPlayer;
        (bool lowAlive, int lowScore) = PredictRemainingBases(low);
        (bool topAlive, int topScore) = PredictRemainingBases(top);

        bool decisive = !lowAlive || !topAlive;
        bool isDraw = !lowAlive && !topAlive && lowScore == topScore;
        int winnerPlayerID = -1;
        if (decisive && !isDraw)
        {
            if (lowAlive != topAlive)
                winnerPlayerID = lowAlive ? low.PlayerID : top.PlayerID;
            else
                // Les deux perdent leur dernière base ce round : le moins "overkillé" l'emporte.
                winnerPlayerID = topScore > lowScore ? top.PlayerID : low.PlayerID;
        }

        Player behind = lowScore < topScore ? low : (topScore < lowScore ? top : null);
        List<int> order = new List<int>(mainBaseResolverIdxs);
        order.Sort((a, b) =>
        {
            int aLast = behind != null && HasBaseInZone(behind, allResolvers[a]) ? 1 : 0;
            int bLast = behind != null && HasBaseInZone(behind, allResolvers[b]) ? 1 : 0;
            return aLast != bLast ? aLast.CompareTo(bLast) : a.CompareTo(b);
        });

        return new RoundOutcome
        {
            Decisive = decisive,
            IsDraw = isDraw,
            WinnerPlayerID = winnerPlayerID,
            MainBaseOrder = order
        };
    }

    // Bases de ce joueur à l'issue de ce round, d'après la planification en cours : vivant tant qu'au
    // moins une survit (bâtiment actif, voir Player.HomeBuildingActive, ou unité-base, voir
    // Player.HomeUnits). score = somme de leurs PV restants prédits — ordonne les combats de Base
    // principale et départage deux joueurs qui perdent leur dernière base le même round. Seul le
    // bâtiment peut finir en négatif (overkill, voir EnqueueBattleCommands, cas TargetKind.Player) :
    // PredictedHealth plafonne une unité à 0.
    static (bool alive, int score) PredictRemainingBases(Player player)
    {
        bool alive = false;
        int score = 0;
        if (player.HomeBuildingActive)
        {
            ZoneCombatResolver resolver = FindResolverForPlayer(player);
            int pending = resolver != null && resolver.pendingPlayerDamage.TryGetValue(player.PlayerID, out int dmg) ? dmg : 0;
            int buildingHP = player.Health - pending;
            score += buildingHP;
            if (buildingHP > 0) alive = true;
        }
        foreach (CreatureLogic unit in player.HomeUnits)
        {
            int unitHP = PredictedHealth(unit);
            score += unitHP;
            if (unitHP > 0) alive = true;
        }
        return (alive, score);
    }

    // Vrai si ce joueur a une base dans la zone de ce resolver : bâtiment actif, ou unité-base
    // (mourante comprise — elle n'est retirée de HomeUnits qu'au DrainPendingDeaths).
    static bool HasBaseInZone(Player player, ZoneCombatResolver resolver)
    {
        if (player.HomeBuildingActive && resolver.zoneView.subZones.Contains(player.MainPArea)) return true;
        foreach (CreatureLogic unit in player.HomeUnits)
            if (resolver.OwnsCreature(unit.BaseID)) return true;
        return false;
    }

    // -------------------------------------------------------------------------
    // ÉTAPES DE LA BATTLE PHASE — Rencontres → Base principale → Bases neutres
    // -------------------------------------------------------------------------

    public enum BattleStage { Encounters, MainBase, NeutralBases }

    public struct BattleStagePlan
    {
        public List<int> EncounterResolverIdxs;
        public List<int> MainBaseResolverIdxs;    // une entrée par zone contenant une base, NON ordonné (voir BuildBattleStagePlan)
        public List<int> NeutralBaseResolverIdxs;
    }

    static bool IsNeutralBaseZone(ZoneCombatResolver resolver)
    {
        foreach (BaseLogic b in BaseLogic.BasesCreatedThisGame.Values)
            if (b.neutralBaseController != null && b.neutralBaseController.zone == resolver.zoneView)
                return true;
        return false;
    }

    // Répartition des zones entre les 3 étapes — calculée UNE SEULE FOIS par Battle Phase, avant la
    // première étape (TurnManager.DelayedBattleStart en solo, GameNetworkManager.
    // SubmitBattleAssignmentServerRpc en réseau), puis réutilisée telle quelle pour les 3 étapes :
    // chaque zone est ainsi jouée exactement une fois par round. Elle dépend de la position des bases
    // (voir AddBaseZoneIdxs), qui peut changer entre deux étapes (unité-base morte, ou relocalisée par
    // ApplyCrossingDispatch) — la recalculer en cours de route ferait rejouer, ou sauter, la zone
    // concernée. Seule retouche autorisée : PromoteNeutralZonesWithBases, qui avance en Base
    // principale une zone de Bases neutres pas encore jouée. Jamais recalculée côté client non plus :
    // le serveur transmet la liste de chaque étape (voir EnqueueStageReconstructedBattleCommands).
    // À ne JAMAIS confondre avec RoundOutcome.MainBaseOrder (mêmes resolvers, mais ORDONNÉS pour le
    // rejeu par ComputeRoundOutcome) : celui-ci décide seulement quelles zones appartiennent à quelle
    // étape, jamais dans quel ordre les rejouer ni qui gagne.
    //
    // Toute zone contenant une base (bâtiment actif ou unité-base, de l'un ou l'autre joueur) va en
    // Base principale — sauf une zone de croisement, voir AddBaseZoneIdxs : c'est la seule étape dont
    // ComputeRoundOutcome connaît l'issue ET qui rejoue ses combats juste avant GameOverCommand. Une
    // zone de Bases neutres, planifiée après lui, pourrait tuer la dernière base d'un joueur sans que
    // la partie ne se termine.
    public static BattleStagePlan BuildBattleStagePlan()
    {
        HashSet<int> mainBaseIdxs = new HashSet<int>();
        AddBaseZoneIdxs(GlobalSettings.Instance.LowPlayer, mainBaseIdxs);
        AddBaseZoneIdxs(GlobalSettings.Instance.TopPlayer, mainBaseIdxs);

        List<int> encounters = new List<int>();
        List<int> neutralBases = new List<int>();
        for (int i = 0; i < allResolvers.Count; i++)
        {
            if (mainBaseIdxs.Contains(i)) continue;
            (IsNeutralBaseZone(allResolvers[i]) ? neutralBases : encounters).Add(i);
        }
        return new BattleStagePlan
        {
            EncounterResolverIdxs = encounters,
            MainBaseResolverIdxs = new List<int>(mainBaseIdxs),
            NeutralBaseResolverIdxs = neutralBases
        };
    }

    // Zones où ce joueur a une base : celle du bâtiment s'il est actif, et celle de chaque unité-base —
    // sauf une zone de croisement (CrossingZoneSlot), qui reste toujours en Rencontres : son combat
    // doit être résolu AVANT CommandMoveTracker.ComputeCrossingDispatch. Classée en Base principale,
    // elle ne serait jouée qu'après ce dispatch, qui trouverait alors les deux camps "survivants" et
    // renverrait chacun à son origine sans qu'ils se soient jamais battus. L'unité-base qui en ressort
    // est rattrapée ensuite par PromoteNeutralZonesWithBases.
    static void AddBaseZoneIdxs(Player player, HashSet<int> mainBaseIdxs)
    {
        if (player.HomeBuildingActive)
        {
            ZoneCombatResolver resolver = FindResolverForPlayer(player);
            if (resolver != null) mainBaseIdxs.Add(resolver.resolverIndex);
        }
        foreach (CreatureLogic unit in player.HomeUnits)
        {
            ZoneCombatResolver resolver = FindForBase(unit.BaseID);
            if (resolver != null && !IsCrossingSlotZone(resolver)) mainBaseIdxs.Add(resolver.resolverIndex);
        }
    }

    static bool IsCrossingSlotZone(ZoneCombatResolver resolver)
    {
        foreach (CrossingZoneSlot slot in CrossingZoneSlot.AllSlots)
            if (slot.Resolver == resolver) return true;
        return false;
    }

    // À appeler juste avant de planifier l'étape Base principale, donc APRÈS
    // CommandMoveTracker.ApplyCrossingDispatch : une unité-base qui sort d'un croisement (vers sa
    // destination ou son origine) peut atterrir dans une zone de Bases neutres, qui ne serait jouée
    // qu'après ComputeRoundOutcome — si elle y mourait en étant la dernière base de son joueur, la
    // partie ne se terminerait qu'au round suivant. Ces zones n'ont pas encore été jouées : les avancer
    // en Base principale ne rejoue ni ne saute rien. Une zone de Rencontres, déjà jouée, n'est jamais
    // déplacée (l'unité n'y combat simplement plus ce round-ci).
    public static void PromoteNeutralZonesWithBases(ref BattleStagePlan plan)
    {
        HashSet<int> baseIdxs = new HashSet<int>();
        AddBaseZoneIdxs(GlobalSettings.Instance.LowPlayer, baseIdxs);
        AddBaseZoneIdxs(GlobalSettings.Instance.TopPlayer, baseIdxs);
        foreach (int idx in baseIdxs)
            if (plan.NeutralBaseResolverIdxs.Remove(idx))
                plan.MainBaseResolverIdxs.Add(idx);
    }

    // Planification (voir OnBattlePhaseStart) limitée à un sous-ensemble de resolvers — permet de
    // planifier une étape à la fois plutôt que toute la Battle Phase d'un coup, pour que la
    // planification d'une étape reflète bien les déplacements/morts déjà appliqués par l'étape
    // précédente (voir TurnManager.DelayedBattleStart / GameNetworkManager.ServerPlanAndBroadcastStage).
    public static void PlanStage(List<int> resolverIdxs)
    {
        foreach (int idx in resolverIdxs)
        {
            try { allResolvers[idx].OnBattlePhaseStart(); }
            catch (System.Exception e)
            {
                Debug.LogError($"[Battle] EXCEPTION dans OnBattlePhaseStart du resolver #{idx} ({allResolvers[idx].name}): {e}");
            }
        }
    }

    // Coeur partagé du rejeu, pour UNE étape — utilisé par le chemin réseau
    // (EnqueueStageReconstructedBattleCommands) et par le chemin solo (EnqueuePlannedBattleCommandsSolo /
    // EnqueueMainBaseBattleCommandsSolo). `resolveSteps` fournit les BattleStepRecord déjà connus pour
    // un resolver donné (reçus du réseau, ou déjà planifiés localement en solo).
    //
    // Vide pendingDamage du resolver juste après son propre EnqueueBattleCommands : les dégâts qu'il
    // contenait viennent d'être réellement appliqués à Health (voir EnqueueBattleCommands) — le
    // laisser rempli ferait compter ces dégâts une deuxième fois dans PredictedHealth/WouldSurvive
    // (qui scannent pendingDamage de TOUS les resolvers) lors de la planification d'une étape
    // ultérieure, avec un risque concret sur ComputeRoundOutcome (unités-bases) et sur l'occupation
    // de rangée (TokenGenerationSO). pendingBaseDamage/pendingPlayerDamage n'ont pas ce problème :
    // rien ne les scanne inter-resolvers de cette façon.
    static void EnqueueStageBattleCommands(List<int> resolverIdxs, System.Func<int, List<BattleStepRecord>> resolveSteps)
    {
        foreach (int idx in resolverIdxs)
        {
            List<BattleStepRecord> steps = resolveSteps(idx) ?? new List<BattleStepRecord>();
            try
            {
                allResolvers[idx].EnqueueBattleCommands(steps);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EnqueueStage] EXCEPTION pour resolver #{idx}: {e}");
            }
            allResolvers[idx].pendingDamage.Clear();
            // Pertes d'aura anticipées : désormais réellement appliquées au rejeu (MarkPendingDeath).
            allResolvers[idx].pendingAttackLoss.Clear();
            allResolvers[idx].pendingMaxHealthLoss.Clear();
        }
    }

    // Étape Base principale : rejoue les zones dans l'ordre décidé par ComputeRoundOutcome
    // (outcome.MainBaseOrder), puis enfile GameOverCommand si le round est décisif.
    static void EnqueueMainBaseBattleCommands(RoundOutcome outcome, System.Func<int, List<BattleStepRecord>> resolveSteps)
    {
        EnqueueStageBattleCommands(outcome.MainBaseOrder, resolveSteps);
        if (outcome.Decisive)
            TurnManager.TriggerGameOver(outcome.WinnerPlayerID, outcome.IsDraw);
    }

    // Chemin solo : chaque resolver de cette étape a déjà planifié son combat (_lastBattleSteps,
    // rempli par OnBattlePhaseStart) — reste à les enfiler.
    public static void EnqueuePlannedBattleCommandsSolo(List<int> resolverIdxs)
    {
        EnqueueStageBattleCommands(resolverIdxs, idx => allResolvers[idx]._lastBattleSteps);
    }

    public static void EnqueueMainBaseBattleCommandsSolo(RoundOutcome outcome)
    {
        EnqueueMainBaseBattleCommands(outcome, idx => allResolvers[idx]._lastBattleSteps);
    }

    void OnDestroy()
    {
        allResolvers.Remove(this);
    }
}
