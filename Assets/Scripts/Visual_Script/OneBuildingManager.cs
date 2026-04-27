using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;

public class OneBuildingManager : MonoBehaviour 
{
    public CardAsset cardAsset;
    public OneCardManager PreviewManager;
    [Header("Text Component References")]
    public TMP_Text HealthText;
    public TMP_Text AttackText;
     
    [Header("Image References")]
    public Image art;
    public Image frame;
    public Image glow;
    public Image MeleeImage;

    public GameObject AttackDamageBG;

    [Header("Combat Indicators")]
    public GameObject MarkedForDeathIndicator;
    public GameObject WillBeDamagedIndicator;
    public TMP_Text pendingDamageText;

    private bool canAttackNow = false;
    public bool CanAttackNow
    {
        get
        {
            return canAttackNow;
        }

        set
        {
            canAttackNow = value;
        }
    }

    private bool canMoveNow = false;
    public bool CanMoveNow
    {
        get
        {
            return canMoveNow;
        }

        set
        {
            canMoveNow = value;
        }
    }

    public int CurrentHealth { get; private set; }

    public BuildingLogic BuildingLogic { get; set; }
    public BuildSpotVisual OriginSpot { get; set; }


    void Awake()
    {
        if (cardAsset != null)
            ReadBuidingFromAsset();
    }

    public void OnBuildingClicked()
    {
        if (TurnManager.Instance == null || !TurnManager.Instance.IsBattlePhase) return;
        if (BuildingLogic == null) return;

        ZoneCombatResolver resolver = ZoneCombatResolver.FindForBuilding(BuildingLogic);
        if (resolver != null)
            resolver.TryRedirectDamageFromBuilding(BuildingLogic);
    }

    public int BaseID {get; set;}
    public void ReadBuidingFromAsset()
    {
        // Change the card graphic sprite
        art.sprite = cardAsset.CardImage;
        HealthText.text = cardAsset.MaxHealth.ToString();
        if(cardAsset.Attack > 0)
        {
            AttackDamageBG.SetActive(true);
            AttackText.text = cardAsset.Attack.ToString();
        }

        if(cardAsset.melee)
        {
            MeleeImage.enabled = true;
        }

        if (PreviewManager != null)
        {
            PreviewManager.cardAsset = cardAsset;
            PreviewManager.ReadCardFromAsset();
        }
        
    }

    public void UpdateBuildingGlow()
    {
        if(TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Battle)
            glow.color = Color.red;
        if(TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Command)
            glow.color = Color.green;
        glow.enabled = CanAttackNow;
    }

    /*public void ResetValues(CardAsset buildingAsset)
    {
        this.cardAsset = cardAsset;
        ReadBaseFromAsset();
    }*/

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        CurrentHealth = Mathf.Max(0, healthAfter);
        DamageEffect.CreateDamageEffect(transform.position, amount);
        HealthText.text = CurrentHealth.ToString();
        // Death is handled by BuildingLogic → BuildingDieCommand
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

}