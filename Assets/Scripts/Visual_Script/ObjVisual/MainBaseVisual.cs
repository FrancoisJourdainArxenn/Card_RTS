using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;


public class MainBaseVisual : MonoBehaviour, ITargetableVisual {

    public Player player;
    public OneBaseManager baseManager;
    public AreaPosition owner;
    public TMP_Text HealthText, MainRessourceText;
    public Image fogOverlay;
    public TMP_Text UpgradeCostText;
    public Button UpgradeButton;
    private int _lastShownUpgradeCost = int.MinValue;

    [HideInInspector] public bool hasBeenSeen = false;
    private bool currentlyVisible = true;
    	
	public void ApplyLookFromAsset()
    {
        if (!currentlyVisible)
            return;
        // Read HP from the authoritative logic value so fog refreshes don't overwrite damage.
        HealthText.text = player.Health.ToString();
        MainRessourceText.text = player.mainRessourceAvailable.ToString();
        baseManager?.RefreshIncomeDisplay(player.playerMainIncome);
        RefreshUpgradeDisplay();

    }

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        Debug.Log("Taking damage: " + amount + " Health after: " + healthAfter);
        if(currentlyVisible)
        {
            VisualFeedbackEffect.CreateDamageEffect(transform.position, amount);
            HealthText.text = healthAfter.ToString();
        }
        // Keep baseManager in sync so any other reader sees the correct value.
        if (baseManager != null && baseManager.HealthText != null)
            baseManager.HealthText.text = healthAfter.ToString();
        if (player == GlobalSettings.Instance.localPlayer)
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
    }

    public void HealDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        int currentHealth = Mathf.Min(player.baseAsset.MaxHealth, healthAfter);
        Debug.Log("Healing damage: " + amount + " Health after: " + currentHealth);
        if(currentlyVisible)
        {
            VisualFeedbackEffect.CreateDamageEffect(transform.position, -amount);
            HealthText.text = currentHealth.ToString();
        }
        // Keep baseManager in sync so any other reader sees the correct value.
        if (baseManager != null && baseManager.HealthText != null)
            baseManager.HealthText.text = currentHealth.ToString();
        if (player == GlobalSettings.Instance.localPlayer)
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
    }

    public void Explode()
    {
        Instantiate(GlobalSettings.Instance.ExplosionPrefab, transform.position, Quaternion.identity);
        Sequence s = DOTween.Sequence();
        s.PrependInterval(2f);
        s.OnComplete(() => GlobalSettings.Instance.GameOverPanel.SetActive(true));
    }

    void OnMouseDown()
    {
        if (TurnManager.Instance == null) return;
        if (PhaseEffectPipeline.IsComplete) return;
        if (BaseLogic.BasesCreatedThisGame.TryGetValue(player.PlayerID, out BaseLogic homeBase))
            PhaseEffectPipeline.OnEntityClicked(homeBase);
    }

    public void UpdateTargetableVisual(bool targetable, bool targeted = false)
    {
        baseManager?.UpdateTargetableVisual(targetable, targeted);
    }

    public void ClearTargetableVisual()
    {
        baseManager?.ClearTargetableVisual();
    }

    public void ApplyFogForObserver(bool hasVision)
    {
        currentlyVisible = hasVision;
        if (hasVision)
        {
            hasBeenSeen = true;
            gameObject.SetActive(true);
            if (fogOverlay != null)
                fogOverlay.gameObject.SetActive(false);
            ApplyLookFromAsset();
        }
        else
        {
            // Invisible si jamais vu, sinon reste visible dans le dernier état connu
            gameObject.SetActive(hasBeenSeen);
            if (fogOverlay != null)
                fogOverlay.gameObject.SetActive(hasBeenSeen);
        }
    }

    void OnEnable()
    {
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

    public void RefreshUpgradeDisplay()
    {
        if (player?.homeBaseLogic == null || UpgradeCostText == null) return;

        int cost = player.homeBaseLogic.CurrentUpgradeCost;
        UpgradeCostText.text = cost.ToString();

        if (cost < _lastShownUpgradeCost && _lastShownUpgradeCost != int.MinValue)
            ValuePopAnimation.Pop(UpgradeCostText.transform);
        _lastShownUpgradeCost = cost;

    if (UpgradeButton != null)
        UpgradeButton.interactable = player == GlobalSettings.Instance.localPlayer
            && player.MainRessourceAvailable >= cost;

    }

    public void OnUpgradeButtonClicked()
    {
        if (player == null) return;
        player.RequestUpgradeBase();
    }



}
