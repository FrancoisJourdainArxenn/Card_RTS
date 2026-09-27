using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GiveMultiStrikeEffectSO")]
public class GiveMultiStrikeSO : EffectSO
{
    public override EffectPriority Priority => EffectPriority.ModifyStats;
    protected override bool IsBuffEffect => true;

    // Valeur fixe (ex. 2 = Double Attaque) : porte attacksForOneTurn à ce total, sans s'additionner
    // si l'effet est appliqué plusieurs fois. 0 = ignoré.
    public int multipleAttacks = 0;

    // Bonus additif classique (Multi-Strike) : s'ajoute à attacksForOneTurn à chaque application.
    // 0 = ignoré.
    public int additionalAttacks = 0;

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

        Log($"{EffectName}: granting Multi-Attack to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
        ApplyEffect(effectInfo, affectedElements, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? _ = null)
    {
        if (target is not CreatureLogic creature) return;

        if (multipleAttacks > 0)
            creature.GrantFixedMultiStrike(multipleAttacks);
        if (additionalAttacks > 0)
            creature.GrantMultiStrike(additionalAttacks);
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() => "gagne Double Attaque";
}
