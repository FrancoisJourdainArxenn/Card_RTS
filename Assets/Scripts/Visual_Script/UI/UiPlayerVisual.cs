using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UiPlayerVisual : MonoBehaviour
{
    [Tooltip("Texte au centre de l’icône (ex. le « X » de maquette).")]

    public TMP_Text mainRessourceText;
    public TMP_Text MainRessourceIncomeText;
    public TMP_Text UpgradeCostText;
    public Image CurrentTierImage;
    public GameObject UpgradeCost;
    public Button UpgradeButton;
    [SerializeField] private UITooltipTrigger ressourceTooltipTrigger;
    [SerializeField] private UITooltipTrigger upgradeTooltipTrigger;

    [Tooltip("Si renseigné, prend la priorité sur owner.")]
    [SerializeField] Player player;

    int lastShownCount = int.MinValue;
    int _lastIncome = int.MinValue;
    int _lastShownUpgradeCost = int.MinValue;
    CardTier _lastShownTier = 0;

    void OnEnable()
    {
        lastShownCount = int.MinValue;
        _lastIncome = int.MinValue;
        _lastShownUpgradeCost = int.MinValue;
        _lastShownTier = 0;
        BaseLogic.OnUpgradeCostChanged += HandleUpgradeCostChanged;
    }

    void OnDisable()
    {
        BaseLogic.OnUpgradeCostChanged -= HandleUpgradeCostChanged;
    }

    private void HandleUpgradeCostChanged(BaseLogic bl)
    {
        if (player == null || bl != player.homeBaseLogic) return;
        RefreshUpgradeDisplay();
    }

    void Start()
    {
        ResolvePlayer();
        lastShownCount = int.MinValue;
        _lastIncome = int.MinValue;

        if (ressourceTooltipTrigger != null)
        {
            ressourceTooltipTrigger.SetDynamicText(() =>
                $"You have {player.mainRessourceAvailable} Ressources to spend.\n" +
                $"You don't lose unspent Ressources.\n" +
                $"The [{player.playerMainIncome}] is your Income (how much Ressources you get each turn).");
        }

        if (upgradeTooltipTrigger != null)
        {
            upgradeTooltipTrigger.SetDynamicText(() =>
            {
                BaseLogic bl = player.homeBaseLogic;
                if (bl.IsMaxTier)
                    return "This base is already at max Tier.";

                int nextTier = (int)bl.CurrentTier + 1;
                return $"Spend {bl.CurrentUpgradeCost} to get to Tier {nextTier}.\n" +
                       $"Tier {nextTier} increases your chances of drawing tier {nextTier} cards.\n" +
                       $"Each tier increases your Income by 1.";
            });
        }
    }

    void ResolvePlayer()
    {
        if (GlobalSettings.Instance?.localPlayer != null)
            player = GlobalSettings.Instance.localPlayer;
    }


    public void RefreshUI()
    {
        ResolvePlayer();
        if (player == null) return;

        int ressource = player.mainRessourceAvailable;
        if (mainRessourceText != null)
        {
            mainRessourceText.text = ressource.ToString();
            if (ressource > lastShownCount && lastShownCount != int.MinValue)
                ValuePopAnimation.Pop(mainRessourceText.transform);
            lastShownCount = ressource;
        }

        int income = player.playerMainIncome;
        if (MainRessourceIncomeText != null)
        {
            MainRessourceIncomeText.text = $"[+ {income}]";
            if (income > _lastIncome && _lastIncome != int.MinValue)
                ValuePopAnimation.Pop(MainRessourceIncomeText.transform);
            _lastIncome = income;
        }

        RefreshUpgradeDisplay();
    }

    public void RefreshUpgradeDisplay()
    {
        if (player?.homeBaseLogic == null || UpgradeCostText == null) return;

        BaseLogic bl = player.homeBaseLogic;
        int cost = bl.CurrentUpgradeCost;
        UpgradeCostText.text = cost.ToString();

        if (cost < _lastShownUpgradeCost && _lastShownUpgradeCost != int.MinValue)
            ValuePopAnimation.Pop(UpgradeCostText.transform);
        _lastShownUpgradeCost = cost;

        if (CurrentTierImage != null)
        {
            CurrentTierImage.sprite = bl.CurrentTierIcon;
            if (bl.CurrentTier != _lastShownTier && _lastShownTier != 0)
                ValuePopAnimation.Pop(CurrentTierImage.transform);
            _lastShownTier = bl.CurrentTier;
        }

        bool isMaxTier = bl.IsMaxTier;
        if (UpgradeCost != null)
            UpgradeCost.SetActive(!isMaxTier);

        // if (!isMaxTier)
        // {
        //     if (UpgradeButton != null)
        //         UpgradeButton.interactable = player.MainRessourceAvailable >= cost;
        // }
    }

    public void OnUpgradeButtonClicked()
    {
        if (player == null) return;
        player.RequestUpgradeBase();
    }

}
