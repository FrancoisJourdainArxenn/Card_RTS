using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DrawCardsSO")]
public class DrawCardsSO : EffectSO
{
    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectParameters parameters,
        EffectVisualData visualData
    )
    {
        if (context.Caster == null)
        {
            Log($"{EffectName}: no caster, cancelled.");
            return;
        }

        Log($"{EffectName}: {context.Caster.name} draws {parameters.Amount} card(s)");
        for (int i = 0; i < parameters.Amount; i++)
            context.Caster.DrawACard(fast: false);
    }

    // Jamais appelées — on bypass ApplyEffect — mais abstraites dans EffectSO
    protected override void ApplyToTarget(ILivable target, int amount, EffectVisualData visualData) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription(EffectParameters parameters) =>
        $"Pioche {parameters.Amount} carte(s)";
}
