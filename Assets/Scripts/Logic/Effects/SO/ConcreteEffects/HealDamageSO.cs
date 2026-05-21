using UnityEngine;

[CreateAssetMenu(menuName = "Effects/HealDamageSO")]
public class HealDamageSO : HealthEffectSO
{
    public override EffectPriority Priority => EffectPriority.HealDamage;
    protected override void ApplyToTarget(ILivable target, int amount, EffectVisualData visualData)
    {
        new HealDamageCommand(target.ID, amount, target.Health + amount, visualData).AddToQueue();
        target.Health += amount;
    }

    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.MaxHealth - target.target.Health;

    public override string GetDescription(EffectParameters parameters) => $"Soigne {parameters.Amount} dégâts";
}
