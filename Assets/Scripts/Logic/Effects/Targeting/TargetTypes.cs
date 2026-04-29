using System.Collections.Generic;

public struct TargetInfo
{
    public TargetObjectType targetType;
    public List<TargetModifier> eligibleTargetModifiers;
    public List<ZoneTargetModifier> eligibleZoneModifiers;
}


public enum TargetObjectType
{
    None,
    Creature,
    Building,
    Base,
    Zone,
    Player,
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

