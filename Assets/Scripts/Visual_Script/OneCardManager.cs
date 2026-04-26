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
    public OneCardManager PreviewManager;
    [Header("Text Component References")]
    public TMP_Text NameText;
    public TMP_Text Type;
    public TMP_Text MainCostText;
    public TMP_Text SecondCostText;
    public TMP_Text DescriptionText;
    public TMP_Text HealthText;
    public TMP_Text AttackText;
    public GameObject ATK_BG;
    
    [Header("Image References")]
    //public Image CardGraphicImage;
    public Image ArtImage;
    public Image FrameImage;
    public Image CardFaceGlowImage;
    //public Image CardBackGlowImage;

    public bool hoverZoomEnabled = false;
    private Vector3 originalScale;

    void Awake()
    {
        originalScale = transform.localScale;
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
        NameText.text = cardAsset.name;
        MainCostText.text = cardAsset.MainCost.ToString();
        SecondCostText.text = cardAsset.SecondCost.ToString();
        // 4) add description
        DescriptionText.text = cardAsset.Description.ToString();
        // 5) Change the card graphic sprite
        ArtImage.sprite = cardAsset.CardImage;

        if (cardAsset.MaxHealth != 0)
        {
            HealthText.text = cardAsset.MaxHealth.ToString();
        }
        if(cardAsset.Attack > 0 || ATK_BG == null)
        {
            ATK_BG.SetActive(true);
            AttackText.text = cardAsset.Attack.ToString();
        } else {ATK_BG.SetActive(false);}

        if (PreviewManager != null)
        {
            // this is a card and not a preview
            // Preview GameObject will have OneCardManager as well, but PreviewManager should be null there
            PreviewManager.cardAsset = cardAsset;
            PreviewManager.ReadCardFromAsset();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if(BuildingShopVisual.IsOpen && hoverZoomEnabled)
            transform.DOScale(originalScale * 1.1f, 0.15f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if(BuildingShopVisual.IsOpen && hoverZoomEnabled)
            transform.DOScale(originalScale, 0.15f);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!BuildingShopVisual.IsOpen) return;
        if (!canBePlayedNow) return;
        GlobalSettings.Instance.buildingShop.OnBuildingSelected(cardAsset);
    }


}
