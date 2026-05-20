using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ModifyStatsSO")]
public class ModifyStatsSO : EffectSO
{
    private int _currentSecondAmount;

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

        _currentSecondAmount = parameters.SecondAmount;
        Log($"{EffectName}: +{parameters.Amount} ATK / +{parameters.SecondAmount} HP to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
        ApplyEffect(effectInfo, affectedElements, parameters, visualData);
    }

    protected override void ApplyToTarget(ILivable target, int amount, EffectVisualData visualData)
    {
        int newAttack = target.Attack + amount;
        int newHealth = target.MaxHealth + _currentSecondAmount;

        new ModifyStatsCommand(target.ID, amount, newAttack, _currentSecondAmount, newHealth, visualData).AddToQueue();

        if (amount != 0) target.Attack += amount;
        if (_currentSecondAmount != 0)
        {
            target.MaxHealth += _currentSecondAmount;
            if (_currentSecondAmount > 0)
                target.Health += _currentSecondAmount;
            else if (target.Health > target.MaxHealth)
                target.Health = target.MaxHealth;
        }
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription(EffectParameters parameters)
    {
        var parts = new List<string>();
        if (parameters.Amount > 0) parts.Add($"+{parameters.Amount} Attaque");
        if (parameters.SecondAmount > 0) parts.Add($"+{parameters.SecondAmount} Vie");
        return string.Join(" / ", parts);
    }
}
