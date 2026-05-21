using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Deck : MonoBehaviour {

    public List<CardAsset> cards = new List<CardAsset>();
    public List<CardAsset> buildings = new List<CardAsset>();

    public WeightedDrawConfig drawConfig;

    public Dictionary<CardAsset, int> timesDrawn = new Dictionary<CardAsset, int>();

    private int _n1, _n2, _n3;

    void Awake()
    {
        timesDrawn = new Dictionary<CardAsset, int>();
        CacheTierCounts();
    }

    private void CacheTierCounts()
    {
        _n1 = 0; _n2 = 0; _n3 = 0;
        foreach (CardAsset card in cards)
        {
            int t = (int)card.tier;
            if (t == 1)      _n1++;
            else if (t == 2) _n2++;
            else if (t == 3) _n3++;
        }
        Debug.Log($"[Deck] Pool — T1:{_n1}  T2:{_n2}  T3:{_n3}  Total:{cards.Count}");
    }

    public CardAsset DrawWeightedCard(int seed, int mainIncome, string playerTag = "?")
    {
        if (drawConfig == null)
        {
            Debug.LogError("[Deck] drawConfig non assigné dans l'Inspector !");
            return null;
        }

        CardAsset drawn = WeightedDraw.Draw(
            cards, timesDrawn, _n1, _n2, _n3,
            mainIncome, seed, drawConfig, playerTag);

        if (drawn != null)
            timesDrawn[drawn] = (timesDrawn.TryGetValue(drawn, out int v) ? v : 0) + 1;

        return drawn;
    }

    public void ResetTimesDrawn() => timesDrawn.Clear();

    public void ShuffleWithSeed(int seed)
    {
        cards.ShuffleWithSeed(seed);
    }

    public void SelectRandomCardFromSeed(int seed)
    {
        cards.SelectRandomCardFromSeed(seed);
    }

    public CardAsset FindBuildingByIndex(int index)
    {
        if (index < 0 || index >= buildings.Count) return null;
        return buildings[index];
    }

}
