using UnityEngine;
using System.Collections.Generic;

public class Deck : MonoBehaviour {

    public DeckSO playerDeck;

    public WeightedDrawConfig drawConfig;

    public Dictionary<CardAsset, int> timesDrawn = new Dictionary<CardAsset, int>();

    private int _n1, _n2, _n3, _n4, _n5;
    private List<CardAsset> _runtimeCards = new List<CardAsset>();

    void Awake()
    {
        timesDrawn = new Dictionary<CardAsset, int>();
        if (playerDeck != null)
            _runtimeCards = new List<CardAsset>(playerDeck.cards);
        CacheTierCounts();
    }

    public void LoadDeck(DeckSO chosenDeck)
    {
        if(chosenDeck == null)
            return;
        playerDeck = chosenDeck;
        _runtimeCards = new List<CardAsset>(chosenDeck.cards);
        timesDrawn.Clear();
        CacheTierCounts();
    }

    private void CacheTierCounts()
    {
        _n1 = 0; _n2 = 0; _n3 = 0; _n4 = 0; _n5 = 0;
        foreach (CardAsset card in _runtimeCards)
        {
            int t = (int)card.tier;
            if (t == 1)      _n1++;
            else if (t == 2) _n2++;
            else if (t == 3) _n3++;
            else if (t == 4) _n4++;
            else if (t == 5) _n5++;
        }
        // Debug.Log($"[Deck] Pool — T1:{_n1}  T2:{_n2}  T3:{_n3}  T4:{_n4}  T5:{_n5}  Total:{_runtimeCards.Count}");
    }

    public CardAsset DrawWeightedCard(int seed, int mainIncome, string playerTag = "?")
    {
        if (drawConfig == null)
        {
            // Debug.LogError("[Deck] drawConfig non assigné dans l'Inspector !");
            return null;
        }

        CardAsset drawn = WeightedDraw.Draw(
            _runtimeCards, timesDrawn, _n1, _n2, _n3, _n4, _n5,
            mainIncome, seed, drawConfig, playerTag);

        if (drawn != null)
            timesDrawn[drawn] = (timesDrawn.TryGetValue(drawn, out int v) ? v : 0) + 1;

        return drawn;
    }

    public void ResetTimesDrawn() => timesDrawn.Clear();

    public void ShuffleWithSeed(int seed)
    {
        _runtimeCards.ShuffleWithSeed(seed);
    }

    public void Shuffle()
    {
        _runtimeCards.Shuffle();
    }


    public void SelectRandomCardFromSeed(int seed)
    {
        _runtimeCards.SelectRandomCardFromSeed(seed);
    }

    public CardAsset FindBuildingByIndex(int index)
    {
        if (index < 0 || index >= playerDeck.buildings.Count) return null;
        return playerDeck.buildings[index];
    }

}
