using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

    public static void ETB(CardAsset ca, EffectContext context)
    {
        if (ca.Effects == null)
            return;

        foreach (CardEffectData data in ca.Effects)
        {
            if (data.Trigger != TriggerType.OnPlay)
                continue;

            Execute(data, context);
        }
    }

    public static void NotifyCreatureDied(CreatureLogic died, Player dyingOwner)
    {
        if (died.ca.Effects != null)
            foreach (CardEffectData data in died.ca.Effects)
            {
                if (data.Trigger != TriggerType.OnDeath)
                    continue;

                Execute(data, new EffectContext { Caster = dyingOwner, Source = died });
            }

        EffectContext eventCtx = new EffectContext { EventSubjectCreature = died };

        // Notifie les entités qui écoutent les morts alliées / ennemies
        FireListeners(TriggerType.OnFriendlyCreatureDies, eventCtx,
            re => re.ContextFactory().Caster == dyingOwner);
        FireListeners(TriggerType.OnEnemyCreatureDies, eventCtx,
            re => re.ContextFactory().Caster != dyingOwner);

        TempEffectTracker.Unregister(died.UniqueCreatureID);
        UnregisterEntity(died.UniqueCreatureID);
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
        Debug.Log($"[EffectRegistry] Executing: {data.EffectName}");

        if (data.Effect == null)
        {
            Debug.Log("[EffectRegistry] No EffectSO");
            return;
        }

        if (data.Condition != null && !data.Condition.Evaluate(context))
        {
            Debug.Log("[EffectRegistry] Condition not met");
            return;
        }

        data.Effect.Execute(data.EffectName, context, data.Effectinfo, data.Effect.EffectVisual);

        if (!data.RequiresPlayerInput)
            TargetingVisualEvents.RaiseAutoEffectTriggered(data, context);
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
}
