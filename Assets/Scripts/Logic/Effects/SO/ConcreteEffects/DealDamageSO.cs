using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DealDamageSO")]
public class DealDamageSO : EffectSO
{
    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectParameters parameters
    )
    {
        List<IIdentifiable> eligibleTargets = new();
        foreach (TargetInfo targetInfo in effectInfo.effectTargets)
            eligibleTargets.AddRange(context.GetEligibleTargets(targetInfo));

        List<ILivable> affectedElements = new();

        if (eligibleTargets.Count == 0)
        {
            bool targetTypeIsNone = effectInfo.effectTargets.Count == 1
                && effectInfo.effectTargets[0].targetType == EffectObjectType.None;
            if (!targetTypeIsNone)
            {
                Log($"{EffectName}: no eligible targets found, effect cancelled.");
                return;
            }
            affectedElements.AddRange(context.GetSingleTargetAffectedElements(null, effectInfo.affectedElements));
        }
        else
        {
            foreach (IIdentifiable target in eligibleTargets)
                affectedElements.AddRange(context.GetSingleTargetAffectedElements(target, effectInfo.affectedElements));
        }

        affectedElements = affectedElements.Distinct().ToList();

        Log($"{EffectName}: {parameters.Amount} damage to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => $"{t.DisplayName}#{t.ID}"))}");

        foreach (ILivable target in affectedElements)
            new DealDamageCommand(target.ID, parameters.Amount, target.Health - parameters.Amount).AddToQueue();
    }

    public override string GetDescription(EffectParameters parameters) => $"Inflige {parameters.Amount} dégâts";
}