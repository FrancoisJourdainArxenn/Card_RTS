using System.Collections.Generic;
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
        Debug.Log($"Executing {EffectName} with parameters: Amount={parameters.Amount}");
        List<IIdentifiable> eligibleTargets = new List<IIdentifiable>();
        foreach (TargetInfo targetInfo in effectInfo.effectTargets)
        {
            eligibleTargets.AddRange(context.GetEligibleTargets(targetInfo));
        }
        
        Debug.Log("Eligible target amount: " + eligibleTargets.Count);
        foreach (ILivable target in eligibleTargets)
        {
            Debug.Log($"Eligible target: {target.GetType().Name} (ID: {target.ID})");
        }

        List<ILivable> affectedElements = new List<ILivable>();
        if (eligibleTargets.Count == 0)
        {
            if (!(effectInfo.effectTargets.Count == 1 && effectInfo.effectTargets[0].targetType == EffectObjectType.None))
            {
                Debug.Log("No eligible targets found for the effect.");
                return;    
            }
            affectedElements.AddRange(context.GetSingleTargetAffectedElements(null, effectInfo.affectedElements));
        }
        
        foreach (ILivable target in eligibleTargets)
        {
            affectedElements.AddRange(context.GetSingleTargetAffectedElements(target, effectInfo.affectedElements));
        }

        Debug.Log("affectedElements amount: " + affectedElements.Count);
        foreach (ILivable target in affectedElements)
        {
            Debug.Log($"affectedElements: {target.GetType().Name} (ID: {target.ID})");
        }

        
        foreach (ILivable target in affectedElements)
        {
            new DealDamageCommand(target.ID, parameters.Amount, target.Health - parameters.Amount).AddToQueue();
            target.Health -= parameters.Amount;
        }
    }
    public override string GetDescription(EffectParameters parameters) => $"Inflige {parameters.Amount} dégâts";
}