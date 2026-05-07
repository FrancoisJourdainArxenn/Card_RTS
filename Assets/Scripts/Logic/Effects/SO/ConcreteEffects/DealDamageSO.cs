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
        Log($"{EffectName}: Execution");
        List<IIdentifiable> eligibleAffectedElements = new();
        foreach (EffectTargetInfo targetInfo in effectInfo.effectTargets)
            eligibleAffectedElements.AddRange(context.GetExecutionAffectedElements(targetInfo));

        List<IIdentifiable> affectedElements = new();

        if (eligibleAffectedElements.Count == 0)
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
            foreach (IIdentifiable target in eligibleAffectedElements)
                affectedElements.AddRange(context.GetSingleTargetAffectedElements(target, effectInfo.affectedElements));
        }

        affectedElements = affectedElements.Distinct().ToList();

        Log($"{EffectName}: {parameters.Amount} damage to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");

        foreach (ILivable target in affectedElements.Cast<ILivable>())
        {
            new DealDamageCommand(target.ID, parameters.Amount, target.Health - parameters.Amount).AddToQueue();
            target.Health -= parameters.Amount;
        }
    }

    public override string GetDescription(EffectParameters parameters) => $"Inflige {parameters.Amount} dégâts";
}