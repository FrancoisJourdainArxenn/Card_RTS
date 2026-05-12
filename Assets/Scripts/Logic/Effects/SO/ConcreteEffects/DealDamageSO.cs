using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DealDamageSO")]
public class DealDamageSO : HealthEffectSO
{
    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectParameters parameters,
        EffectVisualData visualData
    )
    {
        _sourceID = context.Source?.ID ?? -1;
        base.Execute(EffectName, context, effectInfo, parameters, visualData);
    }

    protected override void ApplyToTarget(ILivable target, int amount, EffectVisualData visualData)
    {
        bool hasCustomVfx = visualData != null && (visualData.vfxPrefab != null || visualData.overlayMaterial != null);
        int sourceId = hasCustomVfx ? -1 : _sourceID;
        new DealDamageCommand(target.ID, amount, target.Health - amount, sourceId, hasCustomVfx ? visualData : null).AddToQueue();
        target.Health -= amount;
    }


    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.Health;

    public override string GetDescription(EffectParameters parameters) => $"Inflige {parameters.Amount} dégâts";
}
