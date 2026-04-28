using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DealDamage")]
public class DealDamageSO : EffectSO
{
    public override void Execute(EffectContext context, TargetObjectType targetType, List<TargetModifier> targetModifiers, TargetLocation targetLocation, EffectParameters p)
    {
        foreach (ILivable target in context.ResolveTargets(targetType, targetModifiers, targetLocation))
        {
            new DealDamageCommand(target.ID, p.Amount, target.Health - p.Amount).AddToQueue();
            target.Health -= p.Amount;
        }
    }
    public override string GetDescription(EffectParameters p) => $"Inflige {p.Amount} dégâts";
}