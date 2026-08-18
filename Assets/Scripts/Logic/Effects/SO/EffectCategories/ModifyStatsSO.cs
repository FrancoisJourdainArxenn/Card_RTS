using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ModifyStatsSO")]
public class ModifyStatsSO : EffectSO, IRevertable
{
    [Header("Parameters")]
    public int AttackBonus;
    public int HealthBonus;
    
    [Header("Temporary Effect")]
    public bool IsTempEffect;
    public EffectVisualData RevertVisual;
    protected override int Amount => AttackBonus;
    public bool IsTemporary => IsTempEffect;
    public override EffectPriority Priority => EffectPriority.ModifyStats;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        Log($"{EffectName}: Execution");

        if (effectInfo.useScalingCount)
        {
            ExecuteScaled(EffectName, context, effectInfo, visualData);
            return;
        }

        List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }

        Log($"{EffectName}: {(AttackBonus > 0 ? "+" : "")}{AttackBonus} ATK / {(HealthBonus > 0 ? "+" : "")}{HealthBonus} HP to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
        ApplyEffect(effectInfo, affectedElements, visualData);
    }

    // Scales AttackBonus/HealthBonus by a dynamic count (e.g. "per unit in your zone") instead of
    // applying the fixed bonus. Bypasses ApplyEffect/ApplyToTarget/Revert entirely so the exact
    // scaled delta can be captured for a correct revert, since the generic pipeline never threads
    // an amount through for Uniform/RandomSingleTarget repartition.
    private void ExecuteScaled(string EffectName, EffectContext context, EffectInfo effectInfo, EffectVisualData visualData)
    {
        int count = effectInfo.scalingSource == ScalingSource.SourceShield
            ? context.GetSourceShieldValue()
            : context.GetTargetCount(effectInfo.scalingQuery.targetType, effectInfo.scalingQuery.queries);
        if (count == 0)
        {
            Log($"{EffectName}: scaling count is 0, effect cancelled.");
            return;
        }

        List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }

        int scaledAttack = AttackBonus * count;
        int scaledHealth = HealthBonus * count;

        Log($"{EffectName}: {(scaledAttack > 0 ? "+" : "")}{scaledAttack} ATK / {(scaledHealth > 0 ? "+" : "")}{scaledHealth} HP (x{count}) to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");

        foreach (ILivable target in affectedElements.Cast<ILivable>())
        {
            ApplyStatsDelta(target, scaledAttack, scaledHealth);
            new ModifyStatsCommand(target.ID, scaledAttack, target.Attack, scaledHealth, target.Health, EffectVisual).AddToQueue();

            if (IsTempEffect)
            {
                ILivable t = target;
                TempEffectTracker.Register(t.ID, () =>
                {
                    ApplyStatsDelta(t, -scaledAttack, -scaledHealth);
                    new ModifyStatsCommand(t.ID, -scaledAttack, t.Attack, -scaledHealth, t.Health, RevertVisual).AddToQueue();
                });
            }
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? _ = null)
    {
        ApplyStatsDelta(target, AttackBonus, HealthBonus);
        new ModifyStatsCommand(target.ID, AttackBonus, target.Attack, HealthBonus, target.Health, EffectVisual).AddToQueue();
    }

    public void Revert(ILivable target, int? _ = null)
    {
        ApplyStatsDelta(target, -AttackBonus, -HealthBonus);
        new ModifyStatsCommand(target.ID, -AttackBonus, target.Attack, -HealthBonus, target.Health, RevertVisual).AddToQueue();
    }

    private static void ApplyStatsDelta(ILivable target, int attackDelta, int healthDelta)
    {
        target.Attack += attackDelta;
        target.MaxHealth += healthDelta;
        target.Health += healthDelta;
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription()
    {
        var parts = new List<string>();
        if (AttackBonus > 0) parts.Add($"+{AttackBonus} Attaque");
        if (HealthBonus > 0) parts.Add($"+{HealthBonus} Vie");
        return string.Join(" / ", parts);
    }
}
