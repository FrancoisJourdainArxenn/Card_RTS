using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HeroPortrait : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private Image glowImage;
    [SerializeField] private Image heroPortraitImage;
    [SerializeField] private Image selectedFrame;
    [SerializeField] private Sprite selectedFrameSprite;
    public DeckSO deck;
    [SerializeField] private UITooltipTrigger tooltipTrigger;

    private Sprite normalFrameSprite;
    private static HeroPortrait currentlySelected;

    public static DeckSO SelectedDeck => currentlySelected != null ? currentlySelected.deck : null;

    private void Awake()
    {
        glowImage.enabled = false;
        tooltipTrigger.SetDynamicText(GetHeroTooltipText);

        if (heroPortraitImage == null)
        {
            heroPortraitImage = transform.Find("Hero_Art").GetComponent<Image>();
        }

        if (deck.heroCard != null)
        {
            heroPortraitImage.sprite = deck.heroCard.CardImage;
        }

    }

    private string GetHeroTooltipText()
    {
        CardAsset heroCard = deck.heroCard;
        return $"<b>{heroCard.Name}</b>\n<b>Difficulty:</b><color=#FFFFFFAA> {deck.difficultyLevel}/5\n{deck.deckTooltip}</color>";
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        glowImage.enabled = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        glowImage.enabled = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (currentlySelected == this)
            return;

        if (currentlySelected != null)
            currentlySelected.Deselect();

        currentlySelected = this;
        selectedFrame.enabled = true;
    }

    private void Deselect()
    {
        selectedFrame.enabled = false;
    }
}
