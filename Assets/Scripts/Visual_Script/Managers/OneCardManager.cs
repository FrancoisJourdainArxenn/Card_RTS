using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;
using TMPro;

// holds the refs to all the Text, Images on the card
public class OneCardManager : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{

    public CardAsset cardAsset;
    // public OneCardManager PreviewManager;
    [Header("Text Component References")]
    public TMP_Text NameText;
    public TMP_Text subType;
    public TMP_Text MainCostText;
    public TMP_Text SecondCostText;
    public TMP_Text DescriptionText;
    public TMP_Text HealthText;
    public TMP_Text AttackText;
    public GameObject ATK_BG;
    
    [Header("Image References")]
    public Image ArtImage;
    public Image FrameImage;
    public Image CardFaceGlowImage;
    public bool hoverZoomEnabled = false;
    private Vector3 originalScale;
    private Color _originalAttackColor;
    private Color _originalHealthColor;

    void Awake()
    {
        originalScale = transform.localScale;
        if (AttackText != null) _originalAttackColor = AttackText.color;
        if (HealthText != null) _originalHealthColor = HealthText.color;

        if (cardAsset != null)
            ReadCardFromAsset();
    }

    private bool canBePlayedNow = false;
    public bool CanBePlayedNow
    {
        get
        {
            return canBePlayedNow;
        }

        set
        {
            canBePlayedNow = value;

            CardFaceGlowImage.enabled = value;
        }
    }

    public void ReadCardFromAsset()
    {
        NameText.text = string.IsNullOrEmpty(cardAsset.Name) ? cardAsset.name : cardAsset.Name;
        MainCostText.text = cardAsset.MainCost.ToString();
        SecondCostText.text = cardAsset.SecondCost.ToString();
        subType.text = cardAsset.subType.ToString();
        // 4) add description
        DescriptionText.text = cardAsset.Description.ToString();
        // 5) Change the card graphic sprite
        ArtImage.sprite = cardAsset.CardImage;

        if (cardAsset.MaxHealth != 0)
        {
            HealthText.text = cardAsset.MaxHealth.ToString();
        }
        if(cardAsset.Attack > 0 || cardAsset.Type == CardType.Unit)
        {
            if (ATK_BG != null) ATK_BG.SetActive(true);
            AttackText.text = cardAsset.Attack.ToString();
        } else { if (ATK_BG != null) ATK_BG.SetActive(false); }

    }

    public void OverrideStats(int? attack, int? health, int? maxHealth = null)
    {
        Color buffColor   = GlobalSettings.Instance != null ? GlobalSettings.Instance.statBuffColor   : Color.blue;
        Color debuffColor = GlobalSettings.Instance != null ? GlobalSettings.Instance.statDebuffColor : Color.red;

        if (AttackText != null)
        {
            if (attack.HasValue)
            {
                AttackText.text  = attack.Value.ToString();
                AttackText.color = attack.Value > cardAsset.Attack ? buffColor
                                 : attack.Value < cardAsset.Attack ? debuffColor
                                 : _originalAttackColor;
            }
            else
            {
                AttackText.color = _originalAttackColor;
            }
        }

        if (HealthText != null)
        {
            if (health.HasValue && maxHealth.HasValue)
            {
                HealthText.text  = health.Value.ToString();
                HealthText.color = health.Value > maxHealth.Value ? buffColor
                                 : health.Value < maxHealth.Value ? debuffColor
                                 : _originalHealthColor;
            }
            else
            {
                HealthText.color = _originalHealthColor;
            }
        }
    }

    public void ReadEffectFromAsset(string effectName)
    {
        if (NameText != null)        NameText.text = effectName;
        if (DescriptionText != null) DescriptionText.text = cardAsset.Description.ToString();
        if (ArtImage != null)        ArtImage.sprite = cardAsset.CardImage;
    }


    public void OnPointerEnter(PointerEventData eventData)
    {
        if (BuildingShopVisual.IsOpen && hoverZoomEnabled)
            transform.DOScale(originalScale * 1.1f, 0.15f);

    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (BuildingShopVisual.IsOpen && hoverZoomEnabled)
            transform.DOScale(originalScale, 0.15f);

    }


    public void OnPointerClick(PointerEventData eventData)
    {
        if (!BuildingShopVisual.IsOpen) return;
        if (!canBePlayedNow) return;
        GlobalSettings.Instance.buildingShop.OnBuildingSelected(cardAsset);
    }

}
