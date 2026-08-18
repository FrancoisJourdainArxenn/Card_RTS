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
    Creature,
    Building,
    Base,
    Zone,
    Player,
}
