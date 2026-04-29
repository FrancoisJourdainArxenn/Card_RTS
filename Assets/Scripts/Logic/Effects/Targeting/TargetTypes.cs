using System.Collections.Generic;

public struct TargetInfo
{
    public EffectObjectType targetType;
    public List<TargetModifier> eligibleTargetModifiers;
    public List<ZoneTargetModifier> eligibleZoneModifiers;
}


public enum TargetModifier
{
    Self,
    Friendly,
    Enemy,
    All,
    Melee,
    Ranged,
    // Damaged,
    // Undamaged,
    // Visible,
    // Fogged,
}

public enum ZoneTargetModifier
{
    SameZoneAsSource,
    AdjacentZoneToSource,
    VisibleZone,
}

public enum TargetLocation
{
    Hand,
    Battlefield,
    SelectedZone,
    Units,
}

