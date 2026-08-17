using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ApplyShieldSO")]
public class ApplyShieldSO : EffectSO
{
    [Header("Parameters")]
    public int ShieldAmount;
    protected override int Amount => ShieldAmount;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        Log($"{EffectName}: Execution");
        var targets = GetAffectedElements(context, effectInfo);
        if (targets.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }
        ApplyEffect(effectInfo, targets, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        int value = amount ?? ShieldAmount;
        new ApplyShieldCommand(target.ID, value, visualData).AddToQueue();
        if (target is CreatureLogic creature)
            creature.ApplyShield(value);
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        $"Donne Bouclier {ShieldAmount} à la cible";
}
