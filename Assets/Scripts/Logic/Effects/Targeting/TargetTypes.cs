using System.Collections.Generic;

[System.Serializable]
public struct EffectTargetInfo
{
    public EffectObjectType targetType;
    public bool includesSource;
    public bool onlySource;
    public List<TargetQuery> queries;
    public bool requiresPlayerSelection;
}

[System.Serializable]
public struct TargetQuery
{
    public TargetTeam team;
    public TargetStatusFilter statusFilter;
    public TargetZoneFilter zoneFilter;
}

public enum TargetTeam
{
    All,
    Friendly,
    Enemy,
}

public enum TargetStatusFilter
{
    All,
    Melee,
    Ranged,
    MeleeFirst,
    Damaged,
    // Undamaged,
    // Visible,
    // Fogged,
}

public enum TargetZoneFilter
{
    All,
    SameZoneAsSource,
    SameZoneAsTarget,
    // AdjacentZoneToSource,
    // VisibleZone,
}
