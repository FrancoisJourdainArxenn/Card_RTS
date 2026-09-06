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
        if (target is CreatureLogic creature)
            creature.ApplyShield(value, visualData?.vfxPrefab);
        // "value" est le DELTA de ce gain, pas un total — ApplyShieldCommand l'ajoute à ce qui est déjà
        // affiché plutôt que d'écraser avec un total capturé ici (voir ApplyShieldCommand/VfxManager.
        // AddShieldVfx) : un instantané de ShieldValue pris à cet instant de la planification ne reflète
        // que les gains déjà survenus, jamais la consommation à venir (qui n'a lieu qu'à l'exécution),
        // et écraserait donc l'affichage correct laissé par un ConsumeShieldCommand déjà rejoué.
        new ApplyShieldCommand(target.ID, value, visualData).AddToQueue();
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        $"Donne Bouclier {ShieldAmount} à la cible";
}
