using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DealDamageSO")]
public class DealDamageSO : HealthEffectSO
{
    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectParameters parameters
    )
    {
        _sourceID = context.Source?.ID ?? -1;
        base.Execute(EffectName, context, effectInfo, parameters);
    }
    
    protected override void ApplyToTarget(ILivable target, int amount)
    {
        new DealDamageCommand(target.ID, amount, target.Health - amount, _sourceID).AddToQueue();
        target.Health -= amount;
    }


    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.Health;

    public override string GetDescription(EffectParameters parameters) => $"Inflige {parameters.Amount} dégâts";
}
