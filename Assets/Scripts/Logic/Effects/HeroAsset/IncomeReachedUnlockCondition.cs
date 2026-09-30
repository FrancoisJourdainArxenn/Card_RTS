using UnityEngine;

[CreateAssetMenu(menuName = "Heroes/Unlock Conditions/Income Reached")]
public class IncomeReachedUnlockCondition : HeroUnlockConditionSO
{
    public int RequiredIncome = 5;

    // Income actuel, malus Under Attack compris : le même chiffre que celui affiché au joueur.
    // Le héros peut donc se re-verrouiller en main si l'income redescend.
    public override bool IsUnlocked(Player owner) => owner.playerMainIncome >= RequiredIncome;

    // État en direct, rien à réinitialiser.
    public override void ResetProgress(Player owner) { }

    // Borné à RequiredIncome : tant que le héros reste débloqué, le texte ne change pas, donc pas de petite animation parasite.
    public override string GetDescription(Player owner) =>
        $"Reach {RequiredIncome} Income to Unlock me ({Mathf.Min(owner.playerMainIncome, RequiredIncome)}/{RequiredIncome}).";
}
