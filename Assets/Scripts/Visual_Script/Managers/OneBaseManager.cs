using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class OneBaseManager : MonoBehaviour, ITargetableVisual
{
    public BaseAsset baseAsset;
    public int CurrentHealth { get; private set; }

    [Header("Text Component References")]

    public TMP_Text MainRessourceIncome;
    public TMP_Text SecondRessourceIncome;
    public TMP_Text HealthText;

    [Header("Image References")]
    //public Image CardGraphicImage;
    public Image ArtImage;
    public Image FrameImage;
    public Image CardFaceGlowImage;
    public GameObject Spawner;

    [Header("Damage Indicators")]
    public GameObject MarkedForDeathIndicator;
    public GameObject WillBeDamagedIndicator;
    public TMP_Text pendingDamageText;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (baseAsset != null)
            ReadBaseFromAsset();
    }

    void OnMouseDown()
    {
        if (TurnManager.Instance == null) return;

        IDHolder idHolder = GetComponent<IDHolder>();
        if (idHolder == null) return;
        int id = idHolder.UniqueID;

        if (!EffectTargetingManager.IsComplete)
        {
            IIdentifiable entity = ResolveEntity(id);
            if (entity != null)
                EffectTargetingManager.OnEntityClicked(entity);
            return;
        }

        if (!TurnManager.Instance.IsBattlePhase) return;

        ZoneCombatResolver resolver;

        if (BaseLogic.BasesCreatedThisGame.TryGetValue(id, out BaseLogic bl))
        {
            resolver = bl.neutralBaseController?.zone?.GetComponent<ZoneCombatResolver>();
        }
        else
        {
            Player p = id == GlobalSettings.Instance.LowPlayer.PlayerID
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
            resolver = p?.MainPArea?.parentZone?.GetComponent<ZoneCombatResolver>();
        }

        resolver?.TryRedirectDamageFromBase(id);
    }

    private IIdentifiable ResolveEntity(int id)
    {
        if (BaseLogic.BasesCreatedThisGame.TryGetValue(id, out BaseLogic baseLogic))
            return baseLogic;
        foreach (Player player in Player.Players)
            if (player.ID == id) return player;
        return null;
    }

    public void UpdateTargetableVisual(bool targetable, bool targeted = false)
    {
        ArtImage.color = targetable ? Color.white : Color.gray;
        CardFaceGlowImage.color = targeted ? Color.green : Color.yellow;
        CardFaceGlowImage.enabled = targetable;
    }

    public void ClearTargetableVisual()
    {
        CardFaceGlowImage.enabled = false;
        ArtImage.color = Color.white;
    }

    public void ReadBaseFromAsset()
    {
        CurrentHealth = baseAsset.MaxHealth;
        HealthText.text = CurrentHealth.ToString();
        MainRessourceIncome.text = "+ " + baseAsset.mainRessourceIncome.ToString();
        SecondRessourceIncome.text = "+ " + baseAsset.secondRessourceIncome.ToString();
        ArtImage.sprite = baseAsset.BaseImage;
    }

    public void ResetValues(BaseAsset baseAsset)
    {
        this.baseAsset = baseAsset;
        ReadBaseFromAsset();
    }
    
    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        CurrentHealth = Mathf.Max(0, healthAfter);
        VisualFeedbackEffect.CreateDamageEffect(transform.position, amount);

        if (CurrentHealth <= 0)
        {
            Player ownerPlayer = GetOwnerPlayerFromTag();
            if (ownerPlayer != null && baseAsset != null)
            {
                ownerPlayer.controlledBaseAssets.Remove(baseAsset);
                ownerPlayer.CalculatePlayerIncome();
            }

            Spawner.SetActive(true);
            NeutralBaseVisual baseVisual = Spawner.GetComponent<NeutralBaseVisual>();
            baseVisual.ResetBuildingZone();
            Destroy(gameObject);
        }
        HealthText.text = CurrentHealth.ToString();
    }

    public void HealDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        CurrentHealth = Mathf.Min(baseAsset.MaxHealth, healthAfter);
        // DamageEffect.CreateDamageEffect(transform.position, -amount);
        HealthText.text = CurrentHealth.ToString();
    }

    private Player GetOwnerPlayerFromTag()
    {
        if (GlobalSettings.Instance == null)
            return null;

        if (CompareTag("TopPlayer"))
            return GlobalSettings.Instance.TopPlayer;

        if (CompareTag("LowPlayer"))
            return GlobalSettings.Instance.LowPlayer;

        return null;
    }

    public void ShowPendingDamage(int damage, int currentHealth)
    {
        bool dies = damage >= currentHealth;
        if (MarkedForDeathIndicator != null) MarkedForDeathIndicator.SetActive(dies);
        if (WillBeDamagedIndicator != null)
        {
            WillBeDamagedIndicator.SetActive(!dies);
            if (!dies && pendingDamageText != null)
                pendingDamageText.text = damage.ToString();
        }
    }

    public void ClearPendingDamageIndicator()
    {
        if (MarkedForDeathIndicator != null) MarkedForDeathIndicator.SetActive(false);
        if (WillBeDamagedIndicator != null)  WillBeDamagedIndicator.SetActive(false);
    }

    public void RemoveBaseWithID(int baseUniqueID)
    {
        BaseLogic.BasesCreatedThisGame.Remove(baseUniqueID);
        Player ownerPlayer = GetOwnerPlayerFromTag();
        if (ownerPlayer != null && baseAsset != null)
        {
            ownerPlayer.controlledBaseAssets.Remove(baseAsset);
            ownerPlayer.CalculatePlayerIncome();
        }
        Spawner.SetActive(true);
        NeutralBaseVisual baseVisual = Spawner.GetComponent<NeutralBaseVisual>();
        baseVisual.ResetBuildingZone();
        Destroy(gameObject);
    }
}
