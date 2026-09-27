using System.Collections.Generic;
using UnityEngine;

// Vrai si, dans la zone de la source, le nombre de créatures alliées (source incluse)
// est strictement inférieur au nombre de créatures ennemies présentes dans cette même zone.
[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Outnumbered")]
public class CondOutnumbered : ConditionSO
{
    private static readonly List<TargetQuery> FriendlyQuery = new List<TargetQuery>
    {
        new TargetQuery { team = TargetTeam.Friendly, zoneFilter = TargetZoneFilter.SameZoneAsSource }
    };

    private static readonly List<TargetQuery> EnemyQuery = new List<TargetQuery>
    {
        new TargetQuery { team = TargetTeam.Enemy, zoneFilter = TargetZoneFilter.SameZoneAsSource }
    };

    public override bool Evaluate(EffectContext context)
    {
        int allyCount = context.GetTargetCount(EffectObjectType.Creature, FriendlyQuery, excludeSource: false);
        int enemyCount = context.GetTargetCount(EffectObjectType.Creature, EnemyQuery, excludeSource: false);
        return allyCount < enemyCount;
    }
}
