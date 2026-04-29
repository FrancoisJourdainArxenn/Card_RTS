using System.Collections.Generic;

public struct EffectInfo
{
    List<TargetInfo> effectTargets;
    List<AffectedElement> affectedElements;
    EffectRepartition repartition;
}

public enum EffectRepartition
{
    Uniform,
    Random,
    Selection,
}