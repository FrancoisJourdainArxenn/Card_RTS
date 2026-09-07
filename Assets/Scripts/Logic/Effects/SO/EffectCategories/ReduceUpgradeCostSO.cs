using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ReduceUpgradeCostSO")]
public class ReduceUpgradeCostSO : EffectSO
{
    [Header("Parameters")]
    public int costReduction;

    public override EffectPriority Priority => EffectPriority.GainResources;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        if (context.Caster == null)
        {
            Log($"{EffectName}: no caster, cancelled.");
            return;
        }

        int count = effectInfo.useScalingCount
            ? context.GetTargetCount(effectInfo.scalingQuery.targetType, effectInfo.scalingQuery.queries)
            : 1;
        int totalReduction = costReduction * count;

        Log($"{EffectName}: {context.Caster.name} reduces next tier cost by {totalReduction}");

        if (totalReduction == 0)
        {
            Log($"{EffectName}: scaled amount is 0, cancelled.");
            return;
        }

        if (NetworkSessionData.IsNetworkSession)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            GameNetworkManager.Instance.BroadCastReduceUpgradeCost(context.Caster.playerIndex, totalReduction);
        }
        else
        {
            context.Caster.homeBaseLogic?.ReduceUpgradeCost(totalReduction);
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() => $"Réduit le coût du prochain tier de {costReduction}";
}
