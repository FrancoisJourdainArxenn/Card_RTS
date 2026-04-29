using System.Collections.Generic;

public struct AffectedElement
{
    TargetObjectType affectedElementType;
    AffectedElementZone affectedElementZone;
}
    
public enum AffectedElementType
{
    Target,
    Source,
    Friendly,
    Enemy,
    All,
    Melee,
    Ranged,
}

public enum AffectedElementZone
{
    NoRestriction,
    SameZoneAsSource,
    SameZoneAsTarget,
    AdjacentZonesToSource,
    AdjacentZonesToTarget,
    AllVisibleZones,
}
