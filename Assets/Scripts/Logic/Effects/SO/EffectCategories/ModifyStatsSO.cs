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
    protected override int Amount => _amplifiedAttackBonus;
    public bool IsTemporary => IsTempEffect;
    public override EffectPriority Priority => EffectPriority.ModifyStats;

    private Player _caster;
    private CardAsset _playedCard;
    // Bonus AttackBonus/HealthBonus + amplificateur du caster, capturés une fois à l'exécution pour
    // qu'ApplyToTarget/Revert/ExecuteScaled appliquent et annulent exactement le même montant, même si
    // l'amplificateur change (ou que sa source meurt) entre l'application et l'expiration du buff temporaire.
    private int _amplifiedAttackBonus;
    private int _amplifiedHealthBonus;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        Log($"{EffectName}: Execution");
        _caster = context.Caster;
        _playedCard = context.AmplifierCard;
        (int bonusAttack, int bonusHealth) = _caster != null ? _caster.GetStatBonus(_playedCard) : (0, 0);
        _amplifiedAttackBonus = AttackBonus + bonusAttack;
        _amplifiedHealthBonus = HealthBonus + bonusHealth;

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
            Log($"{EffectName}: {(_amplifiedAttackBonus > 0 ? "+" : "")}{_amplifiedAttackBonus} ATK / {(_amplifiedHealthBonus > 0 ? "+" : "")}{_amplifiedHealthBonus} HP to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
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
            attackBonus = _amplifiedAttackBonus,
            healthBonus = _amplifiedHealthBonus,
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

        // Le bonus d'amplificateur s'ajoute une fois sur le total déjà scalé, pas multiplié par count —
        // même convention que ApplyShieldSO.ExecuteScaled pour le bonus de bouclier du caster.
        int scaledAttack = AttackBonus * count + (_amplifiedAttackBonus - AttackBonus);
        int scaledHealth = HealthBonus * count + (_amplifiedHealthBonus - HealthBonus);

        Log($"{EffectName}: {(scaledAttack > 0 ? "+" : "")}{scaledAttack} ATK / {(scaledHealth > 0 ? "+" : "")}{scaledHealth} HP (x{count}) to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");

        foreach (ILivable target in affectedElements.Cast<ILivable>())
        {
            int attackBefore = target.Attack;
            ApplyStatsDelta(target, scaledAttack, scaledHealth);
            int actualAttackDelta = target.Attack - attackBefore;
            new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, scaledHealth, target.Health, EffectVisual).AddToQueue();
            TrackUpgradeStat(target, scaledAttack, scaledHealth);

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
        ApplyStatsDelta(target, _amplifiedAttackBonus, _amplifiedHealthBonus);
        int actualAttackDelta = target.Attack - attackBefore;
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, _amplifiedHealthBonus, target.Health, EffectVisual).AddToQueue();
        TrackUpgradeStat(target, _amplifiedAttackBonus, _amplifiedHealthBonus);
    }

    public void Revert(ILivable target, int? _ = null)
    {
        int attackBefore = target.Attack;
        ApplyStatsDelta(target, -_amplifiedAttackBonus, -_amplifiedHealthBonus);
        int actualAttackDelta = target.Attack - attackBefore;
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, -_amplifiedHealthBonus, target.Health, RevertVisual).AddToQueue();
    }

    private static void ApplyStatsDelta(ILivable target, int attackDelta, int healthDelta)
    {
        target.Attack += attackDelta;
        target.MaxHealth += healthDelta;
        target.Health += healthDelta;
    }

    // Compte uniquement la part "gain" (upgrade) du delta — un debuff (ATK/Vie négatif) ne doit pas
    // faire progresser la condition de héros "Upgrade your units". Pas appelé depuis Revert : l'expiration
    // d'un buff temporaire ne doit pas retirer le crédit déjà accordé au joueur.
    private static void TrackUpgradeStat(ILivable target, int attackDelta, int healthDelta)
    {
        if (target is not CreatureLogic creature || creature.owner == null) return;
        int upgradeValue = Mathf.Max(0, attackDelta) + Mathf.Max(0, healthDelta);
        if (upgradeValue > 0) creature.owner.matchStats.Add(MatchStatType.UnitsUpgraded, upgradeValue);
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription()
    {
        var parts = new List<string>();
        if (AttackBonus > 0) parts.Add($"+{AttackBonus} Attaque");
        if (HealthBonus > 0) parts.Add($"+{HealthBonus} Vie");
        return string.Join(" / ", parts);
    }

    public override object[] GetDescriptionValues(Player viewer, CardAsset playedCard)
    {
        (int bonusAttack, int bonusHealth) = viewer != null ? viewer.GetStatBonus(playedCard) : (0, 0);
        return new object[] { AttackBonus + bonusAttack, HealthBonus + bonusHealth };
    }
}
