using System.Collections.Generic;

[System.Serializable]
public struct AffectedElement
{
    public EffectObjectType affectedElementType;
    public List<AffectedElementModifier> affectedElementModifiers;
    public AffectedElementZone affectedElementZone;
}

public enum AffectedElementModifier
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
