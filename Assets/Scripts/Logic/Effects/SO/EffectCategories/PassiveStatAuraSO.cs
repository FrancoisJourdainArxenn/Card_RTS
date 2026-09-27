using UnityEngine;

// Aura de stats : accorde AttackBonus/HealthBonus à toutes les cibles désignées par EffectInfo
// (affectedElements/effectTargets), tant que la Condition du CardEffectData est vraie et que la source
// reste en jeu. Contrairement à ModifyStatsSO (montant fixé une fois pour toutes à l'exécution), le
// bonus ici apparaît/disparaît tout seul au fil des recalculs de PassiveAuraManager.
[CreateAssetMenu(menuName = "Effects/Passive/Passive Stat Aura")]
public class PassiveStatAuraSO : EffectSO, IPassiveAuraEffect
{
    [Header("Parameters")]
    public int AttackBonus;
    public int HealthBonus;

    public override EffectPriority Priority => EffectPriority.ModifyStats;

    public object ComputeAuraPayload(EffectContext context, ILivable target) => (AttackBonus, HealthBonus);

    public void ApplyAura(ILivable target, object payload)
    {
        (int attackBonus, int healthBonus) = ((int, int))payload;
        int attackBefore = target.Attack;
        target.Attack    += attackBonus;
        target.MaxHealth += healthBonus;
        target.Health    += healthBonus;
        int actualAttackDelta = target.Attack - attackBefore;
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, healthBonus, target.Health, EffectVisual).AddToQueue();
    }

    public void RevertAura(ILivable target, object payload)
    {
        (int attackBonus, int healthBonus) = ((int, int))payload;
        int attackBefore = target.Attack;
        target.Attack    -= attackBonus;
        target.MaxHealth -= healthBonus;
        target.Health    -= healthBonus;
        int actualAttackDelta = target.Attack - attackBefore;
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, -healthBonus, target.Health, EffectVisual).AddToQueue();
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
        return string.Join(" / ", parts) + " (tant que présent)";
    }
}
