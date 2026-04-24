using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Deck : MonoBehaviour {

    public List<CardAsset> cards = new List<CardAsset>();
    public List<CardAsset> buildings = new List<CardAsset>();

    void Awake()
    {

    }

    public void ShuffleWithSeed(int seed)
    {
        cards.ShuffleWithSeed(seed);
    }
	
    public void SelectRandomCardFromSeed(int seed)
    {
        cards.SelectRandomCardFromSeed(seed);
    }
}
