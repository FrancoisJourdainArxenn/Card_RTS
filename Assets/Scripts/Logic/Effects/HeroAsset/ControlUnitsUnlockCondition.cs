using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Heroes/Unlock Conditions/Control Units")]
public class ControlUnitsUnlockCondition : HeroUnlockConditionSO
{
    [Tooltip("Vide = toutes les unités. Passe par Matches(CreatureLogic) : voit aussi le statut d'unité-base accordé en jeu (unité de départ du deck).")]
    public CardFilterSO Filter;
    public int Threshold = 3;
    [Tooltip("Nom affiché dans la description, ex : \"Flying Units\", \"Home Bases\".")]
    public string UnitLabel = "Units";

    private int GetCount(Player owner) =>
        Filter == null ? owner.Creatures.Count : owner.Creatures.Count(c => Filter.Matches(c));

    // État en direct : le héros se re-verrouille en main si le nombre d'unités redescend sous le seuil.
    public override bool IsUnlocked(Player owner) => GetCount(owner) >= Threshold;

    // État en direct, rien à réinitialiser.
    public override void ResetProgress(Player owner) { }

    // Borné à Threshold : tant que le héros reste débloqué, le texte ne change pas, donc pas de petite animation parasite.
    public override string GetDescription(Player owner) =>
        $"Control {Threshold} {UnitLabel} to Unlock me ({Mathf.Min(GetCount(owner), Threshold)}/{Threshold}).";
}
