using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DealDamageSO")]
public class DealDamageSO : HealthEffectSO
{
    protected override void ApplyToTarget(ILivable target, int amount)
    {
        new DealDamageCommand(target.ID, amount, target.Health - amount).AddToQueue();
        target.Health -= amount;
    }

    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.Health;

    public override string GetDescription(EffectParameters parameters) => $"Inflige {parameters.Amount} dégâts";
}
