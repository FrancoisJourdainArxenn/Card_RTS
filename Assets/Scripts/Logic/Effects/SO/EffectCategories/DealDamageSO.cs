using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DealDamageSO")]
public class DealDamageSO : HealthEffectSO
{
    [Header("Parameters")]
    public int Damage;
    protected override int Amount => Damage;
    public override EffectPriority Priority => EffectPriority.DealDamage;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        _sourceID = context.Source?.ID ?? -1;
        base.Execute(EffectName, context, effectInfo, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        int dmg = amount ?? Damage;
        bool hasCustomVfx = visualData != null && (visualData.vfxPrefab != null || visualData.overlayMaterial != null);
        int sourceId = hasCustomVfx ? -1 : _sourceID;
        new DealDamageCommand(target.ID, dmg, target.Health - dmg, sourceId, hasCustomVfx ? visualData : null).AddToQueue();
        target.Health -= dmg;
    }

    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.Health;

    public override string GetDescription() => $"Inflige {Damage} dégâts";
}
