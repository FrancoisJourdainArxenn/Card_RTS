using System.Collections.Generic;

public static class EffectProcessor
{
    private static Dictionary<TriggerType, List<RegisteredEffect>> _listeners
        = new Dictionary<TriggerType, List<RegisteredEffect>>();

    private struct RegisteredEffect
    {
        public CardEffectData Data;
        public System.Func<EffectContext> ContextFactory;
        public int OwnerID;
    }

    // Appelé dans TurnManager.OnGameStart()
    public static void Reset() => _listeners.Clear();
    
    // Appelé dans le constructeur de CreatureLogic — enregistre les triggers non-OnPlay
    public static void RegisterCreatureEffects(CreatureLogic creature, CardAsset ca)
    {
        if (ca.Effects == null) return;

        foreach (var data in ca.Effects)
        {
            if (data.Trigger == TriggerType.OnPlay) continue;
            AddListener(data, creature.UniqueCreatureID, () => new EffectContext
            {
                Caster = creature.owner,
                SourceCreature = creature
            });
        }
    }

    //Ajouté la logique pour les buildings et les spells

    // Appelé dans PlayACreatureFromHand / PlayASpellFromHand
    public static void ETB(CardAsset ca, EffectContext context)
    {
        if (ca.Effects == null) return;
        foreach (var data in ca.Effects)
        {
            if (data.Trigger != TriggerType.OnPlay) continue;
            TryExecute(data, context);
        }
    }

    // Appelé dans CreatureLogic.Die()
    public static void NotifyCreatureDied(CreatureLogic died, Player dyingOwner)
    {
        // Déclenche les OnDeath de la créature qui meurt
        if (died.ca.Effects != null)
        {
            foreach (var data in died.ca.Effects)
            {
                if (data.Trigger != TriggerType.OnDeath) continue;
                TryExecute(data, new EffectContext { Caster = dyingOwner, SourceCreature = died });
            }
        }
        // Notifie les autres créatures qui écoutent OnAnyCreatureDies etc.
        var eventContext = new EffectContext { EventSubjectCreature = died };
        NotifyFiltered(TriggerType.OnFriendlyCreatureDies, eventContext,
            re => re.ContextFactory().Caster == dyingOwner);
        NotifyFiltered(TriggerType.OnEnemyCreatureDies, eventContext,
            re => re.ContextFactory().Caster != dyingOwner);
        UnregisterEntity(died.UniqueCreatureID);
    }

    // Appelé dans BuildingLogic.Die()
    public static void NotifyBuildingDied(BuildingLogic died, Player dyingOwner)
    {
        // Déclenche les OnDeath de la créature qui meurt
        if (died.ca.Effects != null)
        {
            foreach (var data in died.ca.Effects)
            {
                if (data.Trigger != TriggerType.OnDeath) continue;
                TryExecute(data, new EffectContext { Caster = dyingOwner, SourceBuilding = died });
            }
        }
        // Notifie les autres créatures qui écoutent OnAnyCreatureDies etc.
        var eventContext = new EffectContext { EventSubjectBuilding = died };
        NotifyFiltered(TriggerType.OnFriendlyBuildingDies, eventContext,
            re => re.ContextFactory().Caster == dyingOwner);
        NotifyFiltered(TriggerType.OnEnemyBuildingDies, eventContext,
            re => re.ContextFactory().Caster != dyingOwner);
        UnregisterEntity(died.UniqueBuildingID);
    }

    // Appelé dans Player.OnRegroup()
    public static void NotifyRegroup(Player owner)
        => NotifyFiltered(TriggerType.OnRegroup,
            new EffectContext { Caster = owner },
            re => re.ContextFactory().Caster == owner);

    // Appelé dans Player.OnCommand()
    public static void NotifyCommand(Player owner)
        => NotifyFiltered(TriggerType.OnCommand,
            new EffectContext { Caster = owner },
            re => re.ContextFactory().Caster == owner);

    // Appelé dans Player.OnBattleStart()
    public static void NotifyBattleStart(Player owner)
        => NotifyFiltered(TriggerType.OnBattleStart,
            new EffectContext { Caster = owner },
            re => re.ContextFactory().Caster == owner);

    // Appelé dans Player.OnBattleEnd()
    public static void NotifyBattleEnd(Player owner)
        => NotifyFiltered(TriggerType.OnBattleEnd,
            new EffectContext { Caster = owner },
            re => re.ContextFactory().Caster == owner);

    // Appelé dans Player.OnEndTurn()
    public static void NotifyEndTurn(Player owner)
        => NotifyFiltered(TriggerType.OnEndTurn,
            new EffectContext { Caster = owner },
            re => re.ContextFactory().Caster == owner);

    // Désenregistre tous les effets d'une entité (mort, destruction)
    public static void UnregisterEntity(int ownerID)
    {
        foreach (var list in _listeners.Values)
            list.RemoveAll(re => re.OwnerID == ownerID);
    }

    // ---- Méthodes privées ----
    private static void AddListener(CardEffectData data, int ownerID, System.Func<EffectContext> contextFactory)
    {
        if (!_listeners.ContainsKey(data.Trigger))
            _listeners[data.Trigger] = new List<RegisteredEffect>();
        _listeners[data.Trigger].Add(new RegisteredEffect { Data = data, ContextFactory = contextFactory, OwnerID = ownerID });
    }

    private static void NotifyFiltered(TriggerType trigger, EffectContext baseContext, System.Func<RegisteredEffect, bool> filter)
    {
        if (!_listeners.TryGetValue(trigger, out var list)) return;
        var snapshot = new List<RegisteredEffect>(list);
        foreach (var re in snapshot)
        {
            if (!filter(re)) continue;
            EffectContext context = re.ContextFactory();
            context.EventSubjectCreature = baseContext.EventSubjectCreature ?? context.EventSubjectCreature;
            context.EventSubjectBuilding = baseContext.EventSubjectBuilding ?? context.EventSubjectBuilding;
            TryExecute(re.Data, context);
        }
    }

    private static void TryExecute(CardEffectData data, EffectContext context)
    {
        if (data.Effect == null) return;
        if (data.Condition != null && !data.Condition.Evaluate(context)) return;
        data.Effect.Execute(context, data.TargetType, data.TargetModifiers, data.TargetLocation, data.Parameters);
    }
}