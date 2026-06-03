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
        Log($"[DealDamage] TRIGGERED — {EffectName} | source: {context.Source?.DisplayName ?? "none"} | damage: {Damage}");
        _sourceID = context.Source?.ID ?? -1;
        base.Execute(EffectName, context, effectInfo, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        int dmg = amount ?? Damage;
        Log($"[DealDamage] APPLY — {target.DisplayName} (ID:{target.ID}) HP avant: {target.Health} (dmg:{dmg})");
        bool hasCustomVfx = visualData != null && (visualData.vfxPrefab != null || visualData.overlayMaterial != null);
        int sourceId = hasCustomVfx ? -1 : _sourceID;
        int healthAfter = target.TakeDamage(dmg);
        DeathDrainRecorder.RecordAnim(sourceId, target.ID, dmg, healthAfter);
        new DealDamageCommand(target.ID, dmg, healthAfter, sourceId, hasCustomVfx ? visualData : null).AddToQueue();
    }

    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.Health;

    public override string GetDescription() => $"Inflige {Damage} dégâts";
}
