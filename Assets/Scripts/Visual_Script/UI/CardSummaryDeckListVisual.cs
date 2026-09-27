using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class CardSummaryDeckListVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image artImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image tierImage;
    [SerializeField] private Image rowIcon;
    [SerializeField] private GameObject atkContainer;
    [SerializeField] private TMP_Text atkText;
    [SerializeField] private GameObject healthContainer;
    [SerializeField] private TMP_Text healthText;

    [Header("Row icons (mêmes assets que GlobalSettings en BattleScene)")]
    [SerializeField] private Sprite meleeIcon;
    [SerializeField] private Sprite rangedIcon;

    private CardAsset card;

    public void SetCard(CardAsset card)
    {
        if (card == null) return;
        this.card = card;

        artImage.sprite = card.CardImage;
        nameText.text = string.IsNullOrEmpty(card.Name) ? card.name : card.Name;

        if (VisualManager.Instance != null)
        {
            if (card.IsHero)
            {
                tierImage.sprite = VisualManager.Instance.HeroTierIcon;
            }
            else
            {
                int tierIndex = (int)card.tier - 1;
                if (tierIndex >= 0 && tierIndex < VisualManager.Instance.CardTierIcons.Length)
                    tierImage.sprite = VisualManager.Instance.CardTierIcons[tierIndex];
            }
        }

        bool showStats = card.Type != CardType.Action;

        if (rowIcon != null)
        {
            rowIcon.gameObject.SetActive(showStats);
            if (showStats)
                rowIcon.sprite = card.melee ? meleeIcon : rangedIcon;
        }

        if (atkContainer != null)
            atkContainer.SetActive(showStats);
        if (showStats && atkText != null)
            atkText.text = card.Attack.ToString();

        if (healthContainer != null)
            healthContainer.SetActive(showStats);
        if (showStats && healthText != null)
            healthText.text = card.MaxHealth.ToString();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        MenuCardPreviewUI.Instance?.Show(card);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        MenuCardPreviewUI.Instance?.Hide();
    }
}
