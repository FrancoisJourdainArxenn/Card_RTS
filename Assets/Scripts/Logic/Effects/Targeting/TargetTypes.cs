using System.Collections.Generic;

[System.Serializable]
public struct TargetInfo
{
    public EffectObjectType targetType;
    public List<TargetModifier> eligibleTargetModifiers;
    public List<ZoneTargetModifier> eligibleZoneModifiers;
    public bool requiresPlayerSelection;
}


public enum TargetModifier
{
    Self,
    Friendly,
    Enemy,
    All,
    // Melee,
    // Ranged,
    // Damaged,
    // Undamaged,
    // Visible,
    // Fogged,
}

public enum ZoneTargetModifier
{
    SameZoneAsSource,
    // AdjacentZoneToSource,
    // VisibleZone,
}