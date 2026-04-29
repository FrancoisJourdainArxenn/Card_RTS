using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DealDamageSO")]
public class DealDamageSO : EffectSO
{
    public override void Execute(EffectContext context, EffectInfo effectInfo, TargetObjectType targetType, List<TargetModifier> targetModifiers, TargetLocation targetLocation, EffectParameters p)
    {
        foreach (ILivable target in context.GetAffectedElements(targetType, targetModifiers, targetLocation))
        {
            new DealDamageCommand(target.ID, p.Amount, target.Health - p.Amount).AddToQueue();
            target.Health -= p.Amount;
        }
    }
    public override string GetDescription(EffectParameters p) => $"Inflige {p.Amount} dégâts";
}