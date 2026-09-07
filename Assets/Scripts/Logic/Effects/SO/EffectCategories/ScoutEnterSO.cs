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

        // Le Scout d'une base est une capacité de faction (voir Coalition Base.asset) : elle vaut pour
        // TOUTE base contrôlée par le joueur, capturée ou non — pas seulement celle dont ba.HasScout
        // est coché directement. Le palier de tier se lit sur la home base : une base secondaire ne
        // monte jamais de tier elle-même (CurrentTier y reste figé à T1).
        foreach (BaseLogic playerBase in localPlayer.controlledBases)
        {
            bool hasScout = playerBase.ba.HasScout || localPlayer.baseAsset.HasScout;
            if (!hasScout) continue;
            CardTier minTier = playerBase.ba.HasScout ? playerBase.ba.scoutMinTier : localPlayer.baseAsset.scoutMinTier;
            if (localPlayer.homeBaseLogic == null || localPlayer.homeBaseLogic.CurrentTier < minTier) continue;
            if (playerBase.Zone == null) continue;
            if (zone == playerBase.Zone) continue;
            if (zone.IsAdjacentTo(playerBase.Zone, includeAerialPaths: false))
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
