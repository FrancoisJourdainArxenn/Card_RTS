using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Registre des écouteurs d'effets.
/// - Enregistre / désenregistre les effets des entités (créatures, bâtiments).
/// - Déclenche les triggers instantanés : ETB, OnDeath, triggers réactifs de mort.
/// - Collecte les effets de phase différés (retournés à PhaseEffectPipeline).
/// - Execute() : vérifie la condition → appelle SO.Execute → lève l'event visuel.
/// </summary>
public static class EffectRegistry
{
    private static readonly Dictionary<TriggerType, List<RegisteredEffect>> _listeners
        = new Dictionary<TriggerType, List<RegisteredEffect>>();

    // ID de l'entité source de l'effet en cours d'exécution.
    // Alimenté par Execute() pour que DeathDrainRecorder sache qui a causé un changement de HP.
    public static int CurrentSourceID { get; private set; } = -1;

    private struct RegisteredEffect
    {
        public CardEffectData             Data;
        public System.Func<EffectContext> ContextFactory;
        public int                        OwnerID;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public static void Reset()
    {
        _listeners.Clear();
        TempEffectTracker.Reset();
    }

    // ── Enregistrement ────────────────────────────────────────────────────────

    public static void RegisterCreatureEffects(CreatureLogic creature, CardAsset ca)
    {
        if (ca.Effects == null)
            return;

        foreach (CardEffectData data in ca.Effects)
        {
            if (data.Trigger == TriggerType.OnPlay)
                continue;

            AddListener(data, creature.UniqueCreatureID, () => new EffectContext
                { Caster = creature.owner, Source = creature });
        }
    }

    public static void RegisterBuildingEffects(BuildingLogic building, CardAsset ca)
    {
        if (ca.Effects == null)
            return;

        foreach (CardEffectData data in ca.Effects)
        {
            if (data.Trigger == TriggerType.OnPlay)
                continue;

            AddListener(data, building.UniqueBuildingID, () => new EffectContext
                { Caster = building.owner, Source = building });
        }
    }

    public static void UnregisterEntity(int ownerID)
    {
        foreach (List<RegisteredEffect> list in _listeners.Values)
            list.RemoveAll(re => re.OwnerID == ownerID);
    }

    // ── Triggers instantanés ──────────────────────────────────────────────────

    public static void ETB(CardAsset ca, EffectContext context, List<PendingEffectSelection> preResolvedSelections = null)
    {
        if (ca.Effects == null)
            return;

        foreach (CardEffectData data in ca.Effects)
        {
            if (data.Trigger != TriggerType.OnPlay)
                continue;

            context.SelectedTarget = FindPreResolvedTarget(data, preResolvedSelections);
            Execute(data, context);
        }
    }

    /// <summary>
    /// Reconstruit les sélections OnPlay depuis les tableaux reçus par le réseau (voir
    /// GameNetworkManager.PlayCreatureServerRpc / Player.NetworkPendingPlayCreature).
    /// </summary>
    public static List<PendingEffectSelection> BuildPreResolvedSelections(
        CardAsset ca, int[] effectIndexes, int[] selectedTargetIDs)
    {
        List<PendingEffectSelection> result = new List<PendingEffectSelection>();
        if (ca.Effects == null || effectIndexes == null)
            return result;

        for (int i = 0; i < effectIndexes.Length; i++)
        {
            int idx = effectIndexes[i];
            if (idx < 0 || idx >= ca.Effects.Count)
                continue;

            result.Add(new PendingEffectSelection
            {
                Data           = ca.Effects[idx],
                SelectedTarget = PhaseEffectPipeline.ResolveEntityByID(selectedTargetIDs[i])
            });
        }

        return result;
    }

    public static void NotifyCreatureDied(CreatureLogic died, Player dyingOwner)
    {
        // OnDeath déjà résolu plus tôt pendant la planification de combat
        // (ResolvePredictedBattleDeath) — ne pas le redéclencher ici.
        if (!died.OnDeathResolvedInBattle && died.ca.Effects != null)
            foreach (CardEffectData data in died.ca.Effects)
            {
                if (data.Trigger != TriggerType.OnDeath)
                    continue;

                Execute(data, new EffectContext { Caster = dyingOwner, Source = died });
            }

        EffectContext eventCtx = new EffectContext { EventSubjectCreature = died };

        // Notifie les entités qui écoutent les morts alliées / ennemies. Les listeners portés par
        // une créature ont déjà été résolus par anticipation si cette mort a eu lieu en combat (voir
        // ResolvePredictedBattleDeath → NotifyCreatureDiedPredicted ci-dessous) : on les exclut ici
        // pour ne pas les déclencher une seconde fois. Seuls les listeners portés par un bâtiment
        // restent à déclencher dans ce cas (pas encore couverts par le rejeu prédictif réseau, voir
        // FireListenersPredicted) ; pour une mort hors combat (ReactiveDeathTriggersResolvedInBattle
        // reste false), tout se déclenche ici comme avant.
        FireListeners(TriggerType.OnFriendlyCreatureDies, eventCtx,
            re => re.ContextFactory().Caster == dyingOwner
                && (!died.ReactiveDeathTriggersResolvedInBattle || !IsCreatureListener(re)));
        FireListeners(TriggerType.OnEnemyCreatureDies, eventCtx,
            re => re.ContextFactory().Caster != dyingOwner
                && (!died.ReactiveDeathTriggersResolvedInBattle || !IsCreatureListener(re)));

        NotifyCreatureDeathStats(dyingOwner);

        TempEffectTracker.Unregister(died.UniqueCreatureID);
        UnregisterEntity(died.UniqueCreatureID);
        dyingOwner.RemoveBonusIncomeFromSource(died.UniqueCreatureID); // ← ajouté pour retirer les bonus de revenu liés à la créature morte
        dyingOwner.RemoveBonusShieldFromSource(died.UniqueCreatureID);

    }

    // Résout immédiatement, au moment de la mort prédite en planification de combat (voir
    // CreatureLogic.ResolvePredictedBattleDeath, qui appelle ceci juste après le OnDeath de la
    // créature qui meurt elle-même), les triggers réactifs OnFriendlyCreatureDies/
    // OnEnemyCreatureDies des AUTRES créatures — même philosophie que le OnDeath immédiat déjà en
    // place : le buff (ex: Rex "+1/+0 quand un allié meurt") s'applique au reste de CE combat et
    // est visible dès que la créature meurt visuellement, au lieu d'attendre le drain de fin de
    // Battle (NotifyCreatureDied, qui reste le chemin utilisé pour une mort hors combat et pour les
    // listeners portés par un bâtiment — voir FireListenersPredicted).
    public static void NotifyCreatureDiedPredicted(CreatureLogic died, Player dyingOwner)
    {
        EffectContext eventCtx = new EffectContext { EventSubjectCreature = died };
        FireListenersPredicted(TriggerType.OnFriendlyCreatureDies, eventCtx, died.UniqueCreatureID,
            re => re.ContextFactory().Caster == dyingOwner);
        FireListenersPredicted(TriggerType.OnEnemyCreatureDies, eventCtx, died.UniqueCreatureID,
            re => re.ContextFactory().Caster != dyingOwner);
    }

    // Variante de FireListeners qui exécute chaque listener tout de suite (au lieu d'attendre le
    // drain de fin de Battle) au lieu de le laisser à NotifyCreatureDied, reportée visuellement sous
    // deferKey (l'ID de la créature qui meurt et cause ces réactions) exactement comme le OnDeath de
    // cette même créature juste au-dessus dans ResolvePredictedBattleDeath — la commande visuelle
    // résultante (ex: ModifyStatsCommand sur Rex) se retrouve donc flush au même moment que la
    // CreatureDieCommand de la créature qui meurt (voir CreatureLogic.QueuePendingDeathVisuals).
    //
    // Ne couvre que les listeners portés par une créature : le rejeu déterministe côté client
    // (CreatureLogic.ReplayPredictedTriggerEffect) ne sait résoudre que des CreatureLogic.ca.Effects,
    // comme OnDeath/OnAttack/OnTakeDamage avant lui. Un listener porté par un bâtiment est ignoré ici
    // et reste couvert par le chemin existant (NotifyCreatureDied, à la fin de la Battle phase).
    //
    // L'ID de la créature qui meurt (baseCtx.EventSubjectCreature) est transporté jusqu'au client via
    // PredictedTriggerReplay.EventSubjectID, pour que ReplayPredictedTriggerEffect puisse reconstruire
    // un EventSubjectCreature identique à celui de l'hôte (ex: la Condition CondMyZone de Rex, qui
    // compare la zone du mort à celle de Rex) — sans ça la condition évaluait toujours false côté client.
    private static void FireListenersPredicted(TriggerType trigger, EffectContext baseCtx, int deferKey,
        System.Func<RegisteredEffect, bool> filter)
    {
        if (!_listeners.TryGetValue(trigger, out List<RegisteredEffect> list))
            return;

        bool isNetworkServer = NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer;

        foreach (RegisteredEffect re in new List<RegisteredEffect>(list))
        {
            if (!filter(re)) continue;
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(re.OwnerID, out CreatureLogic listenerCreature))
                continue; // bâtiments — voir NotifyCreatureDied

            // Un listener déjà mort plus tôt dans CE MÊME combat (prédiction déjà résolue, pas encore
            // désenregistré — UnregisterEntity n'a lieu qu'au vrai Die(), bien plus tard) ne doit pas
            // réagir à une mort suivante : contrairement au chemin non prédictif (NotifyCreatureDied),
            // où le traitement séquentiel de ProcessPendingDeaths désenregistre chaque créature avant
            // de traiter la suivante, ici toutes les morts d'une même bataille sont résolues par
            // anticipation avant qu'aucune ne soit désenregistrée.
            if (listenerCreature.IsPendingDeath || listenerCreature.OnDeathResolvedInBattle) continue;

            EffectContext ctx = re.ContextFactory();
            ctx.EventSubjectCreature = baseCtx.EventSubjectCreature ?? ctx.EventSubjectCreature;
            ctx.EventSubjectBuilding = baseCtx.EventSubjectBuilding ?? ctx.EventSubjectBuilding;

            if (!NetworkSessionData.IsNetworkSession)
            {
                Command.RunDeferred(deferKey, () => Execute(re.Data, ctx));
                continue;
            }
            if (!isNetworkServer)
                continue; // rejoué côté client via CreatureLogic.ReplayPredictedTriggerEffect

            int effectIndex = FindEffectIndex(re.OwnerID, re.Data);
            if (effectIndex < 0) continue; // ne devrait pas arriver — garde défensive

            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            System.Random previousRng = EffectSO.CurrentNetworkRng;
            bool alreadyResolving = ZoneCombatResolver.IsResolvingPredictedTrigger;
            try
            {
                EffectSO.SetNetworkRng(new System.Random(seed));
                if (!alreadyResolving) ZoneCombatResolver.BeginResolvingPredictedTrigger();
                Command.RunDeferred(deferKey, () => Execute(re.Data, ctx));
            }
            finally
            {
                if (!alreadyResolving) ZoneCombatResolver.EndResolvingPredictedTrigger();
                EffectSO.SetNetworkRng(previousRng);
            }
            ZoneCombatResolver.RecordPredictedTriggerReplay(re.OwnerID, effectIndex, seed, deferKey,
                ctx.EventSubjectCreature?.UniqueCreatureID ?? -1);
        }
    }

    private static bool IsCreatureListener(RegisteredEffect re) =>
        CreatureLogic.CreaturesCreatedThisGame.ContainsKey(re.OwnerID);

    // Progression des conditions de déblocage héros (All / Ally / Enemy). Séparé de
    // NotifyCreatureDied pour pouvoir être rejoué côté client par SilentDie(), qui ne
    // redéclenche pas les effets OnDeath/triggers mais doit quand même faire avancer
    // ces compteurs — sinon les conditions de héros basées sur les morts ne progressent
    // jamais côté client en session réseau.
    public static void NotifyCreatureDeathStats(Player dyingOwner)
    {
        foreach (Player p in Player.Players)
        {
            p.matchStats.Add(MatchStatType.UnitsDied);
            p.matchStats.Add(p == dyingOwner ? MatchStatType.AllyUnitsDied : MatchStatType.EnemyUnitsDied);
        }
    }

    public static void NotifyBuildingDied(BuildingLogic died, Player dyingOwner)
    {
        if (died.ca.Effects != null)
            foreach (CardEffectData data in died.ca.Effects)
            {
                if (data.Trigger != TriggerType.OnDeath)
                    continue;

                Execute(data, new EffectContext { Caster = dyingOwner, Source = died });
            }

        EffectContext eventCtx = new EffectContext { EventSubjectBuilding = died };

        FireListeners(TriggerType.OnFriendlyBuildingDies, eventCtx,
            re => re.ContextFactory().Caster == dyingOwner);
        FireListeners(TriggerType.OnEnemyBuildingDies, eventCtx,
            re => re.ContextFactory().Caster != dyingOwner);

        UnregisterEntity(died.UniqueBuildingID);
        dyingOwner.RemoveBonusIncomeFromSource(died.UniqueBuildingID); // ← ajouté pour retirer les bonus de revenu liés au bâtiment mort
        dyingOwner.RemoveBonusShieldFromSource(died.UniqueBuildingID);
    }

    // ── Triggers de token ─────────────────────────────────────────────────────
    public static void NotifyTokenCreated(Player creatingPlayer, CreatureLogic tokenOnBoard)
    {
        creatingPlayer.matchStats.Add(MatchStatType.TokensCreated);

        EffectContext eventCtx = new EffectContext { EventSubjectCreature = tokenOnBoard };

        FireListeners(TriggerType.OnTokenCreated, eventCtx,
            re => re.ContextFactory().Caster == creatingPlayer);
    }

    // ── Triggers Reactif ─────────────────────────────────────────────────────

    public static void NotifyCardPlayed(Player playingPlayer, ILivable playedEntity)
    {
        EffectContext eventCtx = new EffectContext();
        if (playedEntity is CreatureLogic creature)
            eventCtx.EventSubjectCreature = creature;
        else if (playedEntity is BuildingLogic building)
            eventCtx.EventSubjectBuilding = building;

        FireListeners(TriggerType.OnCardPlayed, eventCtx,
            re => re.ContextFactory().Caster == playingPlayer && re.ContextFactory().Source != playedEntity);
    }

    public static void NotifyRessourceSpent(Player spendingPlayer)
    {
        EffectContext eventCtx = new EffectContext();

        FireListeners(TriggerType.OnRessourceSpent, eventCtx,
            re => re.ContextFactory().Caster == spendingPlayer);
    }


    // ── Collecte différée (→ PhaseEffectPipeline) ─────────────────────────────

    public static List<PendingEffectSelection> CollectPhaseEffects(Player owner, TriggerType trigger)
    {
        List<PendingEffectSelection> result = new List<PendingEffectSelection>();

        if (!_listeners.TryGetValue(trigger, out List<RegisteredEffect> list))
            return result;

        foreach (RegisteredEffect re in list)
        {
            EffectContext ctx = re.ContextFactory();

            if (ctx.Caster != owner)
                continue;

            List<IIdentifiable> eligibleTargets = new List<IIdentifiable>();
            if (re.Data.RequiresPlayerInput)
                foreach (EffectTargetInfo t in re.Data.Effectinfo.effectTargets
                    .Where(t => t.requiresPlayerSelection))
                    eligibleTargets.AddRange(ctx.GetEligibleTargets(t));

            result.Add(new PendingEffectSelection
            {
                Data              = re.Data,
                Context           = ctx,
                EligibleTargets   = eligibleTargets,
                SourceEntityID    = re.OwnerID,
                EffectIndexInCard = FindEffectIndex(re.OwnerID, re.Data)
            });
        }

        return result;
    }

    // ── Exécution atomique ────────────────────────────────────────────────────

    public static void Execute(CardEffectData data, EffectContext context)
    {
        // Debug.Log($"[EffectRegistry] Executing: {data.EffectName}");

        if (data.Effect == null)
        {
            // Debug.Log("[EffectRegistry] No EffectSO");
            return;
        }

        if (data.Condition != null && !data.Condition.Evaluate(context))
        {
            // Debug.Log("[EffectRegistry] Condition not met");
            return;
        }

        CurrentSourceID = context.Source?.ID ?? -1;

        // Enfilée avant la résolution de l'effet (et non dans RaiseEffectVisualCommand ci-dessous)
        // pour que le VFX de trigger se joue avant que les dégâts/soins/etc. ne soient révélés,
        // tout en restant soumis au même report de combat (Command.DeferForBattleReplay) que le
        // reste — indispensable pour qu'il attende la caméra de bataille comme les autres commandes.
        new PlayTriggerAnimationCommand(data.Trigger, context).AddToQueue();

        data.Effect.Execute(data.EffectName, context, data.Effectinfo, data.Effect.EffectVisual);

        // Passe par la file de commandes (comme ModifyStatsCommand etc.) au lieu de lever
        // l'event directement : pendant une résolution OnDeath anticipée en combat
        // (Command.DeferForBattleReplay), AddToQueue() la reporte et la rejoue au bon moment
        // (voir CreatureLogic.ScheduleBattleDeath) — sinon la popup "carte d'effet" apparaîtrait
        // dès la planification, avant même que la créature ne meure visuellement. Doit rester
        // AVANT la remise à -1 de CurrentSourceID ci-dessous : c'est elle qui sert de clé de
        // report dans Command.AddToQueue().
        // Partie 1 (popup pour effet auto isolé) désactivée :
        // if (!data.RequiresPlayerInput) // effet auto (sans sélection de cible) → déclenche le popup Effect_Triggered
        //     new RaiseEffectVisualCommand(data, context).AddToQueue();

        // Met en file les morts déclenchées par cet effet seulement maintenant, après ses propres
        // commandes "cause" ci-dessus — voir Command.FlushPendingDeaths.
        Command.FlushPendingDeaths();

        CurrentSourceID = -1;
    }

    // ── Utilitaire ────────────────────────────────────────────────────────────

    public static CardAsset GetTokenAsset(int sourceEntityID, int effectIndex)
    {
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(sourceEntityID, out CreatureLogic creature))
            if (creature.ca?.Effects != null && effectIndex >= 0 && effectIndex < creature.ca.Effects.Count)
            {
                TokenGenerationSO tokenSO = creature.ca.Effects[effectIndex].Effect as TokenGenerationSO;
                return tokenSO != null ? tokenSO.TokenToSummon : null;
            }

        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(sourceEntityID, out BuildingLogic building))
            if (building.ca?.Effects != null && effectIndex >= 0 && effectIndex < building.ca.Effects.Count)
            {
                TokenGenerationSO tokenSO = building.ca.Effects[effectIndex].Effect as TokenGenerationSO;
                return tokenSO != null ? tokenSO.TokenToSummon : null;
            }

        return null;
    }

    public static EffectVisualData GetTokenVisualData(int sourceEntityID, int effectIndex)
    {
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(sourceEntityID, out CreatureLogic creature))
            if (creature.ca?.Effects != null && effectIndex >= 0 && effectIndex < creature.ca.Effects.Count)
                return creature.ca.Effects[effectIndex].Effect.EffectVisual;

        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(sourceEntityID, out BuildingLogic building))
            if (building.ca?.Effects != null && effectIndex >= 0 && effectIndex < building.ca.Effects.Count)
                return building.ca.Effects[effectIndex].Effect.EffectVisual;

        return null;
    }

    // ── Privé ─────────────────────────────────────────────────────────────────

    /// <summary>Exécute tous les listeners enregistrés pour ce trigger qui passent le filtre.</summary>
    private static void FireListeners(TriggerType trigger, EffectContext baseCtx,
        System.Func<RegisteredEffect, bool> filter)
    {
        if (!_listeners.TryGetValue(trigger, out List<RegisteredEffect> list))
            return;

        foreach (RegisteredEffect re in new List<RegisteredEffect>(list))
        {
            if (!filter(re))
                continue;

            EffectContext ctx = re.ContextFactory();
            ctx.EventSubjectCreature = baseCtx.EventSubjectCreature ?? ctx.EventSubjectCreature;
            ctx.EventSubjectBuilding = baseCtx.EventSubjectBuilding ?? ctx.EventSubjectBuilding;

            Execute(re.Data, ctx);
        }
    }

    private static void AddListener(CardEffectData data, int ownerID, System.Func<EffectContext> factory)
    {
        if (!_listeners.ContainsKey(data.Trigger))
            _listeners[data.Trigger] = new List<RegisteredEffect>();

        _listeners[data.Trigger].Add(new RegisteredEffect
            { Data = data, ContextFactory = factory, OwnerID = ownerID });
    }

    private static int FindEffectIndex(int ownerID, CardEffectData data)
    {
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(ownerID, out CreatureLogic creature))
            return creature.ca.Effects?.IndexOf(data) ?? -1;

        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(ownerID, out BuildingLogic building))
            return building.ca.Effects?.IndexOf(data) ?? -1;

        return -1;
    }

    /// <summary>Cible choisie par le joueur AVANT l'exécution (voir OnPlayTargetingSession).</summary>
    private static IIdentifiable FindPreResolvedTarget(CardEffectData data, List<PendingEffectSelection> preResolvedSelections)
    {
        if (preResolvedSelections == null)
            return null;

        foreach (PendingEffectSelection sel in preResolvedSelections)
            if (sel.Data == data)
                return sel.SelectedTarget;

        return null;
    }
}
