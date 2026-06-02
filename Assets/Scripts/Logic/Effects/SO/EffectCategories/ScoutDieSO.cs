using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ScoutDieSO")]
public class ScoutDieSO : EffectSO
{
    public override void Execute(string effectName, EffectContext context, EffectInfo effectInfo, EffectVisualData visualData)
    {
        if (context.Source is not CreatureLogic scout) return;
        ScoutEnterSO.ActiveScouts.Remove(scout);
        ZoneEnemyIndicator.RefreshAll();
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;
}
