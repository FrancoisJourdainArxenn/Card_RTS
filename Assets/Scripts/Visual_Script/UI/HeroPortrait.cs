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
    public static HeroPortrait Selected => currentlySelected;
    public Sprite PortraitSprite => heroPortraitImage != null ? heroPortraitImage.sprite : null;

    // Index du mainPool/secondPool choisi dans FactionAsset.mainCardPools/secondCardPools, a
    // transmettre en session reseau (voir NetworkMenu/GameNetworkManager) puisque DeckSO.mainPool
    // /secondPool ne sont mutes qu'en memoire locale par ApplyPoolChoice.
    public static int SelectedMainPoolIndex => GetPoolIndex(main: true);
    public static int SelectedSecondPoolIndex => GetPoolIndex(main: false);

    private static int GetPoolIndex(bool main)
    {
        DeckSO deck = SelectedDeck;
        FactionAsset faction = deck != null && deck.heroCard != null ? deck.heroCard.Faction : null;
        if (faction == null)
            return -1;

        System.Collections.Generic.List<CardPoolSO> pools = main ? faction.mainCardPools : faction.secondCardPools;
        CardPoolSO chosen = main ? deck.mainPool : deck.secondPool;
        return pools != null ? pools.IndexOf(chosen) : -1;
    }

    private void Awake()
    {
        glowImage.enabled = false;
        if (tooltipTrigger != null)
            tooltipTrigger.SetDynamicText(GetHeroTooltipText);

        if (heroPortraitImage == null)
        {
            heroPortraitImage = transform.Find("Hero_Art").GetComponent<Image>();
        }

        if (deck == null)
        {
            Debug.LogWarning($"[HeroPortrait] {name} n'a pas de DeckSO assigné (champ 'Deck').", this);
            return;
        }

        if (deck.heroCard != null)
        {
            heroPortraitImage.sprite = deck.heroCard.CardImage;
        }
    }

    private string GetHeroTooltipText()
    {
        return $"<b>{deck.deckName}</b>\n<b>Difficulty:</b><color=#FFFFFFAA> {deck.difficultyLevel}/5\n{deck.deckTooltip}</color>";
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        glowImage.enabled = true;
        // DeckListPanelController.ShowDeckList_Static(deck);
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

        if (FullDeckListPanelController.Instance != null)
            FullDeckListPanelController.Instance.ShowFullDeck(deck);
    }

    private void Deselect()
    {
        selectedFrame.enabled = false;
    }
}
