using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GrantAttackModifierSO")]
public class GrantAttackModifierSO : EffectSO, IPassiveAuraEffect
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
        _sourceID = context.Source?.ID ?? -1;

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

    // ── IPassiveAuraEffect (TriggerType.Passive) ────────────────────────────────
    // Stateless : ModifierToGrant est un champ sérialisé, jamais modifié à l'exécution — safe à lire
    // depuis plusieurs sources concurrentes. Apply/Revert reposent sur GrantAttackModifier/
    // RemoveAttackModifier, déjà basés sur une liste (CreatureLogic._grantedAttackModifiers) donc déjà
    // safe si plusieurs auras accordent le même modificateur en même temps.
    public object ComputeAuraPayload(EffectContext context, ILivable target) => ModifierToGrant;

    public void ApplyAura(ILivable target, object payload)
    {
        if (target is CreatureLogic creature && payload is AttackModifierSO modifier)
            creature.GrantAttackModifier(modifier);
    }

    public void RevertAura(ILivable target, object payload)
    {
        if (target is CreatureLogic creature && payload is AttackModifierSO modifier)
            creature.RemoveAttackModifier(modifier);
    }
}
