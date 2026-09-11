using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;


[CreateAssetMenu(fileName = "DeckSO", menuName = "Card RTS/Deck Preset")]
public class DeckSO : ScriptableObject
{
    public string deckName;
    public int difficultyLevel;
    [TextArea(2,3)]
    public string deckTooltip;

    [Header("Hero")]
    public CardAsset heroCard;

    [Header("Card Pools")]
    [FormerlySerializedAs("sharedPool")]
    public CardPoolSO mainPool;
    public CardPoolSO secondPool;

    // Deck effectif du heros : fusion des cartes/batiments de mainPool et secondPool.
    public List<CardAsset> cards
    {
        get
        {
            List<CardAsset> merged = new List<CardAsset>();
            if (mainPool != null) merged.AddRange(mainPool.cards);
            if (secondPool != null) merged.AddRange(secondPool.cards);
            return merged;
        }
    }

    public List<CardAsset> buildings
    {
        get
        {
            List<CardAsset> merged = new List<CardAsset>();
            if (mainPool != null) merged.AddRange(mainPool.buildings);
            if (secondPool != null) merged.AddRange(secondPool.buildings);
            return merged;
        }
    }
}
