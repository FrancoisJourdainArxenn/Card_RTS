using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GiveTransportEffectSO")]
public class GiveTransportSO : EffectSO
{
    public override EffectPriority Priority => EffectPriority.ModifyStats;
    // Pas IsBuffEffect, contrairement à Célérité/Multi-Strike : une structure immobile peut garder du
    // sens comme transport (ex: une base qui abrite des unités), elle n'a pas à se déplacer pour ça.

    // Places ajoutées à chaque application — s'additionnent (voir CreatureLogic.GrantTransport).
    public int additionalCapacity = 1;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        Log($"{EffectName}: Execution");
        _sourceID = context.Source?.ID ?? -1;

        List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }

        Log($"{EffectName}: granting Transport +{additionalCapacity} to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
        ApplyEffect(effectInfo, affectedElements, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? _ = null)
    {
        if (target is CreatureLogic creature)
            creature.GrantTransport(additionalCapacity);
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() => $"gagne Transport +{additionalCapacity}";
}
