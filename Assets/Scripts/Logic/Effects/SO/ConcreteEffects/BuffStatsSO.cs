using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/BuffStatsSO")]
public class BuffStatsSO : EffectSO
{
    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectParameters parameters,
        EffectVisualData visualData
    )
    {
        Log($"{EffectName}: Execution");
        List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }

        Log($"{EffectName}: {parameters.Amount} to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
        ApplyEffect(effectInfo, affectedElements, parameters, visualData);
    }

    protected override void ApplyToTarget(ILivable target, int amount, EffectVisualData visualData)
    {
        new BuffStatsCommand(target.ID, amount, target.Attack + amount, visualData).AddToQueue();
        target.Attack += amount;
    }
    protected override bool IsTargetSaturated(EffectTarget target) => false;
    public override string GetDescription(EffectParameters parameters) => $"Ajoute {parameters.Amount} Attack";
}
