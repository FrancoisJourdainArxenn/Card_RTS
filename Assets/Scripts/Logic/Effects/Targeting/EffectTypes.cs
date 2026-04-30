using System.Collections.Generic;

[System.Serializable]
public struct EffectInfo
{
    public List<TargetInfo> effectTargets;
    public List<AffectedElement> affectedElements;
    public EffectRepartition repartition;
}

public enum EffectRepartition
{
    Uniform,
    // Random,
    // Selection,
}

public enum EffectObjectType
{
    None,
    Creature,
    Building,
    Base,
    Zone,
    Player,
}
