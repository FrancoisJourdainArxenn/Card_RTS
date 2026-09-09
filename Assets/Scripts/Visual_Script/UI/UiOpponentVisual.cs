using TMPro;
using UnityEngine;

public class UiOpponentVisual : MonoBehaviour
{
    public TMP_Text opponentRessourceText;
    public TMP_Text opponentIncomeText;
    public TMP_Text opponentUpgradeCostText;

    [Tooltip("Optionnel : objet à désactiver quand l'adversaire est déjà au tier max (ex. le libellé du coût).")]
    public GameObject opponentUpgradeCostRoot;

    int _lastRessource = int.MinValue;
    int _lastIncome = int.MinValue;
    int _lastUpgradeCost = int.MinValue;

    void Update()
    {
        Player opponent = GlobalSettings.Instance?.localPlayer?.otherPlayer;
        if (opponent == null) return;

        int ressource = opponent.mainRessourceAvailable;
        if (opponentRessourceText != null && ressource != _lastRessource)
        {
            opponentRessourceText.text = ressource.ToString();
            _lastRessource = ressource;
        }

        int income = opponent.playerMainIncome;
        if (opponentIncomeText != null && income != _lastIncome)
        {
            opponentIncomeText.text = $"[+ {income}]";
            _lastIncome = income;
        }

        BaseLogic bl = opponent.homeBaseLogic;
        if (bl != null)
        {
            bool isMaxTier = bl.IsMaxTier;
            int cost = bl.CurrentUpgradeCost;

            if (opponentUpgradeCostText != null && cost != _lastUpgradeCost)
            {
                opponentUpgradeCostText.text = $"Cost for next tier: {cost}";
                _lastUpgradeCost = cost;
            }

            if (opponentUpgradeCostRoot != null)
                opponentUpgradeCostRoot.SetActive(!isMaxTier);
        }
    }
}
