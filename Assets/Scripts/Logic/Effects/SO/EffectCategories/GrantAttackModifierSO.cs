using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GrantAttackModifierSO")]
public class GrantAttackModifierSO : EffectSO
{
    [Header("Parameters")]
    public AttackModifierSO ModifierToGrant;

    [Header("Temporary Effect")]
    public bool IsTempEffect;

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

        if (ModifierToGrant == null)
        {
            Log($"{EffectName}: ModifierToGrant non défini, effet annulé.");
            return;
        }

        List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: aucune cible éligible, effet annulé.");
            return;
        }

        Log($"{EffectName}: octroi de {ModifierToGrant.name} à {affectedElements.Count} cible(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
        ApplyEffect(effectInfo, affectedElements, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? _ = null)
    {
        if (target is not CreatureLogic creature) return;

        creature.GrantAttackModifier(ModifierToGrant);

        if (IsTempEffect)
        {
            AttackModifierSO modifier = ModifierToGrant;
            TempEffectTracker.Register(creature.UniqueCreatureID, () => creature.RemoveAttackModifier(modifier));
        }
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        ModifierToGrant != null ? $"gagne {ModifierToGrant.name}" : "gagne une attaque modifiée";
}
