using UnityEngine;

[CreateAssetMenu(menuName = "Heroes/Unlock Conditions/Tier Reached")]
public class TierReachedUnlockCondition : HeroUnlockConditionSO
{
    public CardTier RequiredTier = CardTier.T2;

    // Le tier se lit sur homeBaseLogic, jamais sur une base secondaire (voir Player.cs).
    private CardTier GetTier(Player owner) =>
        owner.homeBaseLogic != null ? owner.homeBaseLogic.CurrentTier : CardTier.T1;

    public override bool IsUnlocked(Player owner) => GetTier(owner) >= RequiredTier;

    // Un tier ne redescend jamais : rien à réinitialiser.
    public override void ResetProgress(Player owner) { }

    // Le tier actuel doit apparaître dans le texte : OneCardManager ne rafraîchit la carte que si ce texte change.
    // Borné à RequiredTier : une fois débloqué, le texte ne change plus, donc pas de petite animation parasite.
    public override string GetDescription(Player owner) =>
        $"Reach Tier {(int)RequiredTier} to Unlock me ({Mathf.Min((int)GetTier(owner), (int)RequiredTier)}/{(int)RequiredTier}).";
}
