using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ApplyShieldSO")]
public class ApplyShieldSO : EffectSO
{
    [Header("Parameters")]
    public int ShieldAmount;
    private Player _caster;
    // Inclut le bonus de bouclier du joueur (voir Player.ShieldBonus) — flux via ce point unique pour
    // que toutes les répartitions d'ApplyEffect (Uniform, Random, RandomMeleeFirst, RandomSingleTarget)
    // en bénéficient automatiquement sans dupliquer la logique.
    protected override int Amount => ShieldAmount + (_caster != null ? _caster.ShieldBonus : 0);
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
        _caster = context.Caster;

        if (effectInfo.useScalingCount)
        {
            ExecuteScaled(EffectName, context, effectInfo, visualData);
            return;
        }

        var targets = GetAffectedElements(context, effectInfo);
        if (targets.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }
        ApplyEffect(effectInfo, targets, visualData);
    }

    // Bypasses ApplyEffect/ApplyToTarget's implicit ShieldAmount so the scaled value isn't written
    // back onto the shared ScriptableObject field — same reasoning as ModifyStatsSO.ExecuteScaled.
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

        List<IIdentifiable> targets = GetAffectedElements(context, effectInfo);
        if (targets.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }

        // Le bonus s'ajoute une fois sur le total déjà scalé, pas multiplié par count.
        int scaledAmount = ShieldAmount * count + (_caster != null ? _caster.ShieldBonus : 0);
        Log($"{EffectName}: Shield {scaledAmount} (x{count}) to {targets.Count} target(s) — {string.Join(", ", targets.Select(t => t.DisplayName))}");

        foreach (ILivable target in targets.Cast<ILivable>())
        {
            ApplyToTarget(target, visualData, scaledAmount);
            QueueSourceVfx(visualData);
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        int value = amount ?? Amount;
        // On capture ShieldValue juste après l'application (donc le total réel affiché, pas le delta
        // "value") pour que la commande visuelle montre toujours le bouclier vraiment accordé à cet
        // instant — même si un combat prédit plus tard dans la même séquence le consomme entièrement
        // avant que la file de commandes n'ait rejoué ce gain (voir ApplyShieldCommand/ConsumeShieldCommand).
        int displayAmount = value;
        if (target is CreatureLogic creature)
        {
            creature.ApplyShield(value, visualData?.vfxPrefab);
            displayAmount = creature.ShieldValue;
        }
        new ApplyShieldCommand(target.ID, displayAmount, visualData).AddToQueue();
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        $"Donne Bouclier {ShieldAmount} à la cible";
}
