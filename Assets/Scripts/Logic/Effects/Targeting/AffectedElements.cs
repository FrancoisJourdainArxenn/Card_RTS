using System.Collections.Generic;

[System.Serializable]
public struct AffectedElement
{
    public EffectObjectType affectedElementType;
    public bool includesTarget;
    public bool includesSource;
    public bool includesEventSubject;
    public List<TargetQuery> queries;
}
