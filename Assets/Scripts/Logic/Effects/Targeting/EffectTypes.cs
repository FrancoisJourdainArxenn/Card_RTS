using System.Collections.Generic;

[System.Serializable]
public struct EffectInfo
{
    public List<EffectTargetInfo> effectTargets;
    public List<AffectedElement> affectedElements;
    public EffectRepartition repartition;
    public bool useScalingCount;
    public CountQuery scalingQuery;
    public ScalingSource scalingSource;
}

[System.Serializable]
public struct CountQuery
{
    public EffectObjectType targetType;
    public List<TargetQuery> queries;
}

public enum ScalingSource
{
    TargetCount,   // compte scalingQuery (comportement existant)
    SourceShield,  // ShieldValue de la source de l'effet
    CasterTier,    // tier actuel du Caster (lu sur homeBaseLogic)
}

public enum EffectRepartition
{
    Uniform,
    Random,
    RandomMeleeFirst,
    RandomSingleTarget,
    // Selection,
}

public enum EffectObjectType
{
    // Valeurs explicites : c'est l'entier qui est sérialisé dans les assets (1 = ancien Building,
    // retiré) — ne jamais renuméroter.
    Creature = 0,
    Base     = 2,
    Zone     = 3,
    Player   = 4,
}
