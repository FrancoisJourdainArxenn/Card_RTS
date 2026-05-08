using UnityEngine;

[CreateAssetMenu(menuName = "Effects/HealDamageSO")]
public class HealDamageSO : HealthEffectSO
{
    protected override void ApplyToTarget(ILivable target, int amount)
    {
        new HealDamageCommand(target.ID, amount, target.Health + amount).AddToQueue();
        target.Health += amount;
    }

    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.MaxHealth - target.target.Health;

    public override string GetDescription(EffectParameters parameters) => $"Soigne {parameters.Amount} dégâts";
}
