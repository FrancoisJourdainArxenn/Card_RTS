using UnityEngine;

[CreateAssetMenu(menuName = "Effects/HealDamageSO")]
public class HealDamageSO : HealthEffectSO
{
    [Header("Parameters")]
    public int HealAmount;
    protected override int Amount => HealAmount + (_caster != null ? _caster.GetHealBonus(_playedCard) : 0);
    public override EffectPriority Priority => EffectPriority.HealDamage;

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        int heal = amount ?? Amount;
        int newHealth = Mathf.Min(target.Health + heal, target.MaxHealth);
        int amountHealed = newHealth - target.Health;
        new HealDamageCommand(target.ID, amountHealed, newHealth, visualData).AddToQueue();
        target.Health = newHealth;
    }

    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.MaxHealth - target.target.Health;

    public override string GetDescription() => $"Soigne {HealAmount} dégâts";

    public override object[] GetDescriptionValues(Player viewer, CardAsset playedCard) =>
        new object[] { HealAmount + (viewer != null ? viewer.GetHealBonus(playedCard) : 0) };
}
