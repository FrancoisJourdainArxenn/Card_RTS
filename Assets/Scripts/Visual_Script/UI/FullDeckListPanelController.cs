using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

// Vit sur FullDeckList_Panel (copie de Deck_ListPanel.prefab dans MenuScene) : consultation en
// lecture seule du deck complet et fusionne du hero (mainPool + secondPool, voir
// DeckSO.cards), independant de DeckListPanelController qui gere le hover/click-preview
// MainDeck/SecondDeck sur son propre DeckList_Panel.
public class FullDeckListPanelController : MonoBehaviour
{
    public static FullDeckListPanelController Instance { get; private set; }

    [SerializeField] private RectTransform content;
    [SerializeField] private TMP_Text deckNameText;
    [SerializeField] private CardSummaryDeckListVisual cardSummaryPrefab;

    [Header("Recap (Hero & Decks)")]
    [SerializeField] private Image heroImage;
    [SerializeField] private CardPoolSlot mainSlot;
    [SerializeField] private CardPoolSlot secondSlot;

    private readonly List<CardSummaryDeckListVisual> spawnedEntries = new List<CardSummaryDeckListVisual>();

    private void Awake()
    {
        Instance = this;
    }

    // Appele par HeroPortrait.OnPointerClick a chaque nouvelle selection de hero.
    public void ShowFullDeck(DeckSO deck)
    {
        if (deck == null)
            return;

        ClearEntries();

        if (deckNameText != null)
            deckNameText.text = deck.deckName;
        if (heroImage != null)
            heroImage.sprite = deck.heroCard != null ? deck.heroCard.CardImage : null;
        if (mainSlot != null)
            mainSlot.SetCardPool(deck.mainPool);
        if (secondSlot != null)
            secondSlot.SetCardPool(deck.secondPool);

        if (deck.heroCard != null)
            SpawnEntry(deck.heroCard);
        foreach (CardAsset card in deck.cards)
            SpawnEntry(card);
    }

    private void SpawnEntry(CardAsset card)
    {
        CardSummaryDeckListVisual entry = Instantiate(cardSummaryPrefab, content);
        entry.SetCard(card);
        spawnedEntries.Add(entry);
    }

    private void ClearEntries()
    {
        foreach (CardSummaryDeckListVisual entry in spawnedEntries)
            Destroy(entry.gameObject);
        spawnedEntries.Clear();
    }
}
