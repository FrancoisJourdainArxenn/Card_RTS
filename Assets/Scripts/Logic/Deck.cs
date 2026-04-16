using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Deck : MonoBehaviour {

    public List<CardAsset> cards = new List<CardAsset>();

    void Awake()
    {

    }

    public void ShuffleWithSeed(int seed)
    {
        cards.ShuffleWithSeed(seed);
    }
	
}
