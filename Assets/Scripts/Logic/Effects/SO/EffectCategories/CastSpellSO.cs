using UnityEngine;

[CreateAssetMenu(menuName = "Effects/CastSpellSO")]
public class CastSpellSO : EffectSO
{
    [Header("Parameters")]
    public CardAsset SpellToCast;
    public int CastCount = 1;
    public override EffectPriority Priority => EffectPriority.CastSpell;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        if (context.Caster == null || SpellToCast == null)
        {
            Log($"{EffectName}: missing caster or spell asset, cancelled.");
            return;
        }

        Log($"{EffectName}: {context.Caster.name} casts {SpellToCast.name} x{CastCount}");

        if (visualData != null && context.Source != null)
            new PlayEffectVisualCommand(context.Source.ID, visualData).AddToQueue();

        for (int i = 0; i < CastCount; i++)
        {
            EffectRegistry.ETB(SpellToCast, new EffectContext
            {
                Caster = context.Caster,
                Source = context.Source,
                Target = context.Target,
                EventSubjectCreature = context.EventSubjectCreature,
                EventSubjectBuilding = context.EventSubjectBuilding
            });
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        SpellToCast == null
            ? "Lance un sort"
            : $"Lance {SpellToCast.name}" + (CastCount > 1 ? $" x{CastCount}" : "");
}
