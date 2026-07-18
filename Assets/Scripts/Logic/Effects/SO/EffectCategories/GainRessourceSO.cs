using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GainResourcesSO")]
public class GainResourcesSO : EffectSO
{
    [Header("Parameters")]
    public int resourceAmount;
    public bool IsRecurring;

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

        int sourceID = context.Source?.ID ?? -1;

        int count = effectInfo.useScalingCount
            ? context.GetTargetCount(effectInfo.scalingQuery.targetType, effectInfo.scalingQuery.queries)
            : 1;
        int totalAmount = resourceAmount * count;

        Log($"{EffectName}: {context.Caster.name} gains {totalAmount} {(IsRecurring ? "income" : "resources")} (sourceID={sourceID})");

        if (totalAmount == 0)
        {
            Log($"{EffectName}: scaled amount is 0, cancelled.");
            return;
        }

        if (NetworkSessionData.IsNetworkSession)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            GameNetworkManager.Instance.BroadCastGainRessources(context.Caster.playerIndex, totalAmount, IsRecurring, sourceID);
        }
        else
        {
            if (IsRecurring)
            {
                if (sourceID != -1)
                    context.Caster.AddBonusIncomeFromSource(sourceID, totalAmount);
                else
                    context.Caster.AddBonusIncome(totalAmount);
            }
            else
                context.Caster.GetBonusRessources(totalAmount);
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        IsRecurring
            ? $"Gagne {resourceAmount} revenu(s) par tour"
            : $"Gagne {resourceAmount} ressource(s)";
}
