using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ReplaceCardEffectSO")]
public class ReplaceCardEffectSO : EffectSO
{
    [Header("Parameters")]
    public CardAsset TargetCard;
    public EffectSO OldEffect;
    public EffectSO NewEffect;

    // Instances ayant effectivement remplacé un effet, pour pouvoir tout annuler au
    // démarrage de la partie suivante (voir ResetAll) — TargetCard est un ScriptableObject
    // partagé, sa mutation runtime survit sinon à la fin de la partie : pas de reload de
    // domaine/scène entre deux parties dans la même session Play (voir TurnManager.OnGameStart,
    // qui fait déjà le même nettoyage pour EffectRegistry/CreatureAttackVisual).
    private static readonly HashSet<ReplaceCardEffectSO> _applied = new();

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

        if (replacedCount > 0)
            _applied.Add(this);

        Log($"{EffectName}: {replacedCount} entrée(s) remplacée(s) sur {TargetCard.name}.");
    }

    // Annule tous les remplacements effectués depuis le dernier reset, en remettant OldEffect
    // partout où NewEffect est actuellement posé. A appeler au démarrage d'une nouvelle partie.
    public static void ResetAll()
    {
        foreach (ReplaceCardEffectSO instance in _applied)
        {
            if (instance.TargetCard?.Effects == null)
                continue;

            foreach (CardEffectData data in instance.TargetCard.Effects)
            {
                if (data.Effect == instance.NewEffect)
                    data.Effect = instance.OldEffect;
            }
        }

        _applied.Clear();
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        $"Vos {TargetCard?.name} génèrent maintenant de meilleures inventions.";
}
