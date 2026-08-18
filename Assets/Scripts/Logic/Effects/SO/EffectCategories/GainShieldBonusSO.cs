using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GainShieldBonusSO")]
public class GainShieldBonusSO : EffectSO
{
    [Header("Parameters")]
    public int ShieldBonusAmount;

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
        if (sourceID == -1)
        {
            Log($"{EffectName}: no source, cancelled.");
            return;
        }

        Log($"{EffectName}: {context.Caster.name} gains +{ShieldBonusAmount} shield bonus (sourceID={sourceID})");

        if (NetworkSessionData.IsNetworkSession)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            GameNetworkManager.Instance.BroadCastShieldBonus(context.Caster.playerIndex, ShieldBonusAmount, sourceID);
        }
        else
        {
            context.Caster.AddBonusShieldFromSource(sourceID, ShieldBonusAmount);
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        $"Vos valeurs de Bouclier ont toutes +{ShieldBonusAmount}";
}
