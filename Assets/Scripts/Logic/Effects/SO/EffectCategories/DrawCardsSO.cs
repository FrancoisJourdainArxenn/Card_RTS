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

        Log($"{EffectName}: {context.Caster.name} draws {CardCount} card(s)");

        if (NetworkSessionData.IsNetworkSession)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            for (int i = 0; i < CardCount; i++)
                GameNetworkManager.Instance.BroadCastDrawCard(context.Caster.playerIndex);
        }
        else
        {
            for (int i = 0; i < CardCount; i++)
                context.Caster.DrawACard(fast: false);
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() => $"Pioche {CardCount} carte(s)";
}
