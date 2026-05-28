using UnityEngine;

[CreateAssetMenu(menuName = "Effects/HealDamageSO")]
public class HealDamageSO : HealthEffectSO
{
    [Header("Parameters")]
    public int HealAmount;
    protected override int Amount => HealAmount;
    public override EffectPriority Priority => EffectPriority.HealDamage;

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        int heal = amount ?? HealAmount;
        new HealDamageCommand(target.ID, heal, target.Health + heal, visualData).AddToQueue();
        target.Health += heal;
    }

    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.MaxHealth - target.target.Health;

    public override string GetDescription() => $"Soigne {HealAmount} dégâts";
}
