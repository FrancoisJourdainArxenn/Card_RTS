using System.Collections.Generic;

[System.Serializable]
public struct EffectInfo
{
    public List<EffectTargetInfo> effectTargets;
    public List<AffectedElement> affectedElements;
    public EffectRepartition repartition;
}

public enum EffectRepartition
{
    Uniform,
    Random,
    RandomMeleeFirst,
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
