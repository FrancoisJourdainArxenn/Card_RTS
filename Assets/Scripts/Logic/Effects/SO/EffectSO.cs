using System.Collections.Generic;
using UnityEngine;

public abstract class EffectSO : ScriptableObject
{
    public abstract void Execute(
        EffectContext context,
        EffectInfo effectInfo,
        TargetObjectType targetType,
        List<TargetModifier> targetModifiers,
        TargetLocation targetLocation,
        EffectParameters parameters
    );
    public virtual string GetDescription(EffectParameters p) => "";
}