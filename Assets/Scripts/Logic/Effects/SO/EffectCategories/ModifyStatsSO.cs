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

    [Header("Permanent Type/Name Buff (rest of the game)")]
    [Tooltip("Ex: Robots.asset — toute unité créée plus tard qui correspond recevra aussi ce bonus.")]
    public CardFilterSO PersistentFilter;
    [Tooltip("La cible touchée maintenant + toute unité créée plus tard du même nom recevront aussi ce bonus.")]
    public bool MatchSameNameAsTarget;
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
            Log($"{EffectName}: no eligible targets found right now.");
        }
        else
        {
            Log($"{EffectName}: {(AttackBonus > 0 ? "+" : "")}{AttackBonus} ATK / {(HealthBonus > 0 ? "+" : "")}{HealthBonus} HP to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
            ApplyEffect(effectInfo, affectedElements, visualData);
        }

        // Enregistré même si personne n'est éligible maintenant (ex: Roach joué sans Zergling sur le
        // plateau) : un PersistentFilter fixe (ex: Zergling.asset) ne dépend pas des cibles actuelles,
        // donc l'annuler ici perdrait le buff pour toutes les unités qui apparaîtront plus tard —
        // contraire au but même de l'effet. MatchSameNameAsTarget reste conditionné à une cible trouvée
        // (voir RegisterPersistentBuffIfNeeded), faute de nom à enregistrer sinon.
        RegisterPersistentBuffIfNeeded(context, affectedElements);
    }

    // Enregistre ce bonus sur le Caster pour qu'il s'applique aussi à toute CreatureLogic créée plus
    // tard (voir CreatureLogic constructor) — pas seulement aux cibles touchées maintenant. Stocké côté
    // Player (pas sur le CardAsset, partagé entre joueurs) pour ne pas buffer l'adversaire.
    private void RegisterPersistentBuffIfNeeded(EffectContext context, List<IIdentifiable> affectedElements)
    {
        if (context.Caster == null) return;
        if (PersistentFilter == null && !MatchSameNameAsTarget) return;

        CardFilterSO filter = PersistentFilter;
        if (MatchSameNameAsTarget)
        {
            CreatureLogic firstCreature = affectedElements.OfType<CreatureLogic>().FirstOrDefault();
            if (firstCreature == null) return;

            filter = ScriptableObject.CreateInstance<CardFilterSO>();
            filter.filterByName = true;
            filter.requiredName = firstCreature.ca.Name;
        }

        context.Caster.permanentCreatureBuffs.Add(new PermanentCreatureBuff
        {
            filter = filter,
            attackBonus = AttackBonus,
            healthBonus = HealthBonus,
        });
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
            int attackBefore = target.Attack;
            ApplyStatsDelta(target, scaledAttack, scaledHealth);
            int actualAttackDelta = target.Attack - attackBefore;
            new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, scaledHealth, target.Health, EffectVisual).AddToQueue();

            if (IsTempEffect)
            {
                ILivable t = target;
                TempEffectTracker.Register(t.ID, () =>
                {
                    int revertAttackBefore = t.Attack;
                    ApplyStatsDelta(t, -scaledAttack, -scaledHealth);
                    int actualRevertAttackDelta = t.Attack - revertAttackBefore;
                    new ModifyStatsCommand(t.ID, actualRevertAttackDelta, t.Attack, -scaledHealth, t.Health, RevertVisual).AddToQueue();
                });
            }
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? _ = null)
    {
        int attackBefore = target.Attack;
        ApplyStatsDelta(target, AttackBonus, HealthBonus);
        int actualAttackDelta = target.Attack - attackBefore;
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, HealthBonus, target.Health, EffectVisual).AddToQueue();
    }

    public void Revert(ILivable target, int? _ = null)
    {
        int attackBefore = target.Attack;
        ApplyStatsDelta(target, -AttackBonus, -HealthBonus);
        int actualAttackDelta = target.Attack - attackBefore;
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, -HealthBonus, target.Health, RevertVisual).AddToQueue();
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
