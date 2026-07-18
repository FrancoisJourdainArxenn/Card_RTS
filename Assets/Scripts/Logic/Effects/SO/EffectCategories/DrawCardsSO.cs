using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DrawCardsSO")]
public class DrawCardsSO : EffectSO
{
    [Header("Parameters")]
    public int CardCount;
    public override EffectPriority Priority => EffectPriority.DrawCards;

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
        int totalCount = CardCount * count;

        Log($"{EffectName}: {context.Caster.name} draws {totalCount} card(s)");

        if (totalCount == 0)
        {
            Log($"{EffectName}: scaled count is 0, cancelled.");
            return;
        }

        if (NetworkSessionData.IsNetworkSession)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            for (int i = 0; i < totalCount; i++)
                GameNetworkManager.Instance.BroadCastDrawCard(context.Caster.playerIndex);
        }
        else
        {
            for (int i = 0; i < totalCount; i++)
                context.Caster.DrawACard(fast: false);
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() => $"Pioche {CardCount} carte(s)";
}
