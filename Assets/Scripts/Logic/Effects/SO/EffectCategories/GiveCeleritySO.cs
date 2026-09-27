using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GiveCelerityEffectSO")]
public class GiveCeleritySO : EffectSO, IPassiveAuraEffect
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
        _sourceID = context.Source?.ID ?? -1;

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

    // ── IPassiveAuraEffect (TriggerType.Passive) ────────────────────────────────
    // GrantCelerity/RemoveCelerity comptent les sources actives (voir CreatureLogic) — safe même si
    // plusieurs auras Célérité indépendantes touchent la même créature en même temps.
    public object ComputeAuraPayload(EffectContext context, ILivable target) => true;

    public void ApplyAura(ILivable target, object payload)
    {
        if (target is CreatureLogic creature)
            creature.GrantCelerity();
    }

    public void RevertAura(ILivable target, object payload)
    {
        if (target is CreatureLogic creature)
            creature.RemoveCelerity();
    }
}
