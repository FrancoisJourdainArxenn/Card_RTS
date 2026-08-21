using UnityEngine;
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

    [Header("Shared Pool")]
    public CardPoolSO sharedPool;

    public List<CardAsset> cards => sharedPool != null ? sharedPool.cards : new List<CardAsset>();
    public List<CardAsset> buildings => sharedPool != null ? sharedPool.buildings : new List<CardAsset>();
}
