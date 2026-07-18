using UnityEngine;
using System.Collections.Generic;


[CreateAssetMenu(fileName = "DeckSO", menuName = "Card RTS/Deck Preset")]
public class DeckSO : ScriptableObject
{
    public string deckName;
    
    [Header("Hero")]
    public CardAsset heroCard;
    public List<CardAsset> cards = new List<CardAsset>();
    public List<CardAsset> buildings = new List<CardAsset>();
}
