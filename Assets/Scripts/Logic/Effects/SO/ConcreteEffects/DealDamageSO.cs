using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DealDamageSO")]
public class DealDamageSO : EffectSO
{
    public override void Execute(
        EffectContext context,
        EffectInfo effectInfo,
        EffectObjectType targetType,
        List<TargetModifier> targetModifiers,
        TargetLocation targetLocation,
        EffectParameters p
    )
    {
        List<IIdentifiable> eligibleTargets = new List<IIdentifiable>();
        foreach (TargetInfo targetInfo in effectInfo.effectTargets)
        {
            eligibleTargets.AddRange(context.GetEligibleTargets(targetInfo));
        }
        Debug.Log("Eligible targets: " + string.Join(", ", eligibleTargets));

        List<ILivable> affectedElements = new List<ILivable>();
        foreach (ILivable target in eligibleTargets)
        {
            affectedElements.AddRange(context.GetSingleTargetAffectedElements(target, effectInfo.affectedElements));
        }

        foreach (ILivable target in affectedElements)
        {
            new DealDamageCommand(target.ID, p.Amount, target.Health - p.Amount).AddToQueue();
            target.Health -= p.Amount;
        }
    }
    public override string GetDescription(EffectParameters p) => $"Inflige {p.Amount} dégâts";
}