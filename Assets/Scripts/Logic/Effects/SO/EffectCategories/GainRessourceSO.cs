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
        Log($"{EffectName}: {context.Caster.name} gains {resourceAmount} {(IsRecurring ? "income" : "resources")} (sourceID={sourceID})");

        if (NetworkSessionData.IsNetworkSession)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            GameNetworkManager.Instance.BroadCastGainRessources(context.Caster.playerIndex, resourceAmount, IsRecurring, sourceID);
        }
        else
        {
            if (IsRecurring)
            {
                if (sourceID != -1)
                    context.Caster.AddBonusIncomeFromSource(sourceID, resourceAmount);
                else
                    context.Caster.AddBonusIncome(resourceAmount);
            }
            else
                context.Caster.GetBonusRessources(resourceAmount);
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        IsRecurring
            ? $"Gagne {resourceAmount} revenu(s) par tour"
            : $"Gagne {resourceAmount} ressource(s)";
}
