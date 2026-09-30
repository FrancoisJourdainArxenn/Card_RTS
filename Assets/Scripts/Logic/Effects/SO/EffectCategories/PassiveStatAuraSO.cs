using UnityEngine;

// Aura de stats : accorde AttackBonus/HealthBonus à toutes les cibles désignées par EffectInfo
// (affectedElements/effectTargets), tant que la Condition du CardEffectData est vraie et que la source
// reste en jeu. Contrairement à ModifyStatsSO (montant fixé une fois pour toutes à l'exécution), le
// bonus ici apparaît/disparaît tout seul au fil des recalculs de PassiveAuraManager.
[CreateAssetMenu(menuName = "Effects/Passive/Passive Stat Aura")]
public class PassiveStatAuraSO : EffectSO, IPassiveAuraEffect, IUpdatablePassiveAura
{
    [Header("Parameters")]
    public int AttackBonus;
    public int HealthBonus;

    // Ajouté au bonus à chaque début de tour que la source passe en jeu (voir CreatureLogic.TurnsInPlay).
    // Une source morte puis rejouée est une nouvelle instance : elle repart du bonus de base.
    [Header("Growth (per turn in play)")]
    public int AttackGrowthPerTurn;
    public int HealthGrowthPerTurn;

    public override EffectPriority Priority => EffectPriority.ModifyStats;

    public object ComputeAuraPayload(EffectContext context, ILivable target)
    {
        int turns = context.Source is CreatureLogic source ? source.TurnsInPlay : 0;
        return (AttackBonus + AttackGrowthPerTurn * turns, HealthBonus + HealthGrowthPerTurn * turns);
    }

    public void ApplyAura(ILivable target, object payload)
    {
        (int attackBonus, int healthBonus) = ((int, int))payload;
        Shift(target, attackBonus, healthBonus);
    }

    public void RevertAura(ILivable target, object payload)
    {
        (int attackBonus, int healthBonus) = ((int, int))payload;
        Shift(target, -attackBonus, -healthBonus);
    }

    public void UpdateAura(ILivable target, object oldPayload, object newPayload)
    {
        (int oldAttack, int oldHealth) = ((int, int))oldPayload;
        (int newAttack, int newHealth) = ((int, int))newPayload;
        Shift(target, newAttack - oldAttack, newHealth - oldHealth);
    }

    // Perte d'aura plafonnée, jamais mortelle — voir EffectSO.ShiftStatsCapped.
    private void Shift(ILivable target, int attackDelta, int healthDelta)
    {
        (int actualAttackDelta, int actualHealthDelta) = ShiftStatsCapped(target, attackDelta, healthDelta);
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, actualHealthDelta, target.Health, EffectVisual).AddToQueue();
    }

    // Jamais appelés pour un effet Passive (PassiveAuraManager n'appelle que ComputeAuraPayload/
    // ApplyAura/RevertAura) — implémentés seulement pour satisfaire EffectSO.
    public override void Execute(string EffectName, EffectContext context, EffectInfo effectInfo, EffectVisualData visualData) =>
        Debug.LogWarning($"{EffectName}: PassiveStatAuraSO ne doit être utilisé qu'avec TriggerType.Passive.");
    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription()
    {
        System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
        if (AttackBonus != 0) parts.Add($"{(AttackBonus > 0 ? "+" : "")}{AttackBonus} Attaque");
        if (HealthBonus != 0) parts.Add($"{(HealthBonus > 0 ? "+" : "")}{HealthBonus} Vie");
        string description = string.Join(" / ", parts) + " (tant que présent)";
        if (AttackGrowthPerTurn != 0 || HealthGrowthPerTurn != 0)
            description += $", +{AttackGrowthPerTurn}/+{HealthGrowthPerTurn} par tour en jeu";
        return description;
    }
}
