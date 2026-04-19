using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;

public class OneCreatureManager : MonoBehaviour 
{
    public CardAsset cardAsset;
    public OneCardManager PreviewManager;
    [Header("Text Component References")]
    public TMP_Text HealthText;
    public TMP_Text AttackText;         
    [Header("Image References")]
    public Image CreatureGraphicImage;
    public Image CreatureFrameImage;
    public Image CreatureGlowImage;
    public Image MeleeImage;

    [Header("Combat Indicators")]
    public GameObject MarkedForDeathIndicator;
    public GameObject WillBeDamagedIndicator;
    public TMP_Text pendingDamageText;


    void Awake()
    {
        if (cardAsset != null)
            ReadCreatureFromAsset();
    }

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
    public int BaseID {get; set;}
    public void ReadCreatureFromAsset()
    {
        // Change the card graphic sprite
        CreatureGraphicImage.sprite = cardAsset.CardImage;

        AttackText.text = cardAsset.Attack.ToString();
        HealthText.text = cardAsset.MaxHealth.ToString();

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

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount > 0)
        {
            DamageEffect.CreateDamageEffect(transform.position, amount);
            HealthText.text = healthAfter.ToString();
            GetComponentInParent<TableVisual>()?.ownerArea?.RefreshAreaStats();
        }
    }

    public void UpdateCreatureGlow()
    {
        if (CanAttackNow)
            CreatureGlowImage.color = Color.red;
        else if (CanMoveNow)
            CreatureGlowImage.color = Color.green;
        CreatureGlowImage.enabled = CanAttackNow || CanMoveNow;
    }

    public void UpdateTargetableVisual(bool targetable)
    {
        CreatureGraphicImage.color = targetable ? Color.white : Color.gray;
    }

    public void SetGray(bool gray)
    {
        CreatureGraphicImage.color = gray ? Color.gray : Color.white;
        CreatureFrameImage.color = gray ? Color.gray : Color.white;
    }

    public void ShowPendingDamage(int damage, int currentHealth)
    {
        bool dies = damage >= currentHealth;
        if (MarkedForDeathIndicator != null) MarkedForDeathIndicator.SetActive(dies);
        if (WillBeDamagedIndicator != null) 
        {
            WillBeDamagedIndicator.SetActive(!dies);
            if (!dies)
            {
                if (pendingDamageText != null) pendingDamageText.text = damage.ToString();
            }
        }
    }

    public void OnCreatureClicked()
    {
        Debug.Log($"[Click] IsBattlePhase={TurnManager.Instance?.IsBattlePhase}");
        if (!TurnManager.Instance.IsBattlePhase) return;

        IDHolder idHolder = GetComponent<IDHolder>();
        Debug.Log($"[Click] IDHolder={idHolder?.UniqueID}");
        if (idHolder == null) return;

        bool found = CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out CreatureLogic creature);
        Debug.Log($"[Click] CreatureFound={found}");
        if (!found) return;

        Player localPlayer = GlobalSettings.Instance.localPlayer;
        bool isOwn = localPlayer.table.CreaturesInPlay.Contains(creature);
        Debug.Log($"[Click] IsOwnCreature={isOwn}, BaseID={BaseID}");
        if (isOwn) return;

        ZoneCombatResolver resolver = ZoneCombatResolver.FindForBase(BaseID);
        Debug.Log($"[Click] Resolver={resolver}");
        resolver?.TryRedirectDamageFrom(creature);
    }


    public void ClearPendingDamageIndicator()
    {
        if (MarkedForDeathIndicator != null) MarkedForDeathIndicator.SetActive(false);
        if (WillBeDamagedIndicator != null)  WillBeDamagedIndicator.SetActive(false);
    }

}
