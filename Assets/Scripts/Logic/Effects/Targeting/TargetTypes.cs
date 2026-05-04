using System.Collections.Generic;

[System.Serializable]
public struct EffectTargetInfo
{
    public EffectObjectType targetType;
    public List<TargetDetails> targetDetails;
    public bool requiresPlayerSelection;
}

[System.Serializable]
public struct TargetDetails
{
    public TargetTeam team;
    public TargetStatusFilter statusFilter;
    public TargetZoneFilter zoneFilter;
}

public enum TargetTeam
{
    All,
    Self,
    Friendly,
    Enemy,
}

public enum TargetStatusFilter
{
    All,
    Melee,
    Ranged,
    // Damaged,
    // Undamaged,
    // Visible,
    // Fogged,
}

public enum TargetZoneFilter
{
    All,
    SameZoneAsSource,
    // AdjacentZoneToSource,
    // VisibleZone,
}