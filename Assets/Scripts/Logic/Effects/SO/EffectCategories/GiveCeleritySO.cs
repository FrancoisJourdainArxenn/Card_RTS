using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GiveCelerityEffectSO")]
public class GiveCeleritySO : EffectSO
{
    public override EffectPriority Priority => EffectPriority.ModifyStats;
    protected override bool IsBuffEffect => true;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
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

        Log($"{EffectName}: granting Celerity to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
        ApplyEffect(effectInfo, affectedElements, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? _ = null)
    {
        if (target is CreatureLogic creature)
            creature.GrantCelerity();
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() => "gagne la Célérité";
}
