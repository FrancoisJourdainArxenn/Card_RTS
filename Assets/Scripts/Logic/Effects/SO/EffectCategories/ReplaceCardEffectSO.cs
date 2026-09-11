using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ReplaceCardEffectSO")]
public class ReplaceCardEffectSO : EffectSO
{
    [Header("Parameters")]
    public CardAsset TargetCard;
    public EffectSO OldEffect;
    public EffectSO NewEffect;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData)
    {
        if (TargetCard == null || TargetCard.Effects == null || NewEffect == null)
        {
            Log($"{EffectName}: TargetCard/NewEffect manquant, annulé.");
            return;
        }

        int replacedCount = 0;
        foreach (CardEffectData data in TargetCard.Effects)
        {
            if (data.Effect == OldEffect)
            {
                data.Effect = NewEffect;
                replacedCount++;
            }
        }

        Log($"{EffectName}: {replacedCount} entrée(s) remplacée(s) sur {TargetCard.name}.");
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        $"Vos {TargetCard?.name} génèrent maintenant de meilleures inventions.";
}
