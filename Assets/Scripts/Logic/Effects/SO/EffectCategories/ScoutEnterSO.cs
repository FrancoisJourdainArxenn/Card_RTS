using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ScoutEnterSO")]
public class ScoutEnterSO : EffectSO
{
    public static readonly HashSet<CreatureLogic> ActiveScouts = new HashSet<CreatureLogic>();

    public static bool HasScoutAdjacentTo(ZoneLogic zone, Player localPlayer)
    {
        foreach (CreatureLogic scout in ActiveScouts)
        {
            if (scout.owner != localPlayer) continue;
            if (scout.Zone == null) continue;
            if (zone == scout.Zone) continue;
            if (zone.IsAdjacentTo(scout.Zone, includeAerialPaths: false))
                return true;
        }
        return false;
    }

    public override void Execute(string effectName, EffectContext context, EffectInfo effectInfo, EffectVisualData visualData)
    {
        if (context.Source is not CreatureLogic scout) return;
        ActiveScouts.Add(scout);
        ZoneEnemyIndicator.RefreshAll();
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;
}
