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
        List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }

        Log($"{EffectName}: {(AttackBonus > 0 ? "+" : "")}{AttackBonus} ATK / {(HealthBonus > 0 ? "+" : "")}{HealthBonus} HP to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
        ApplyEffect(effectInfo, affectedElements, visualData);
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
