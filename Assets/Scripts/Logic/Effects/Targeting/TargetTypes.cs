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
    public CardFilterSO cardFilter;
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
    NonShielded,
    HighestHealth,
    LowestHealth,
    // Base principale au sens "condition de défaite" : unité-base ou bâtiment principal actif — voir
    // EffectContext.IsHomeBaseTarget. Valeur sérialisée dans les assets : ne jamais la renuméroter.
    HomeBase = 8,
    // Undamaged,
    // Visible,
    // Fogged,
}

public enum TargetZoneFilter
{
    All,
    SameZoneAsSource,
    SameZoneAsTarget,
    AdjacentToSource,
    AdjacentToTarget,
    // VisibleZone,
}
