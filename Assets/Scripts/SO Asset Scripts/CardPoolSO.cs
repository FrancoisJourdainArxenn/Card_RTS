using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CardPoolSO", menuName = "Card RTS/Card Pool")]
public class CardPoolSO : ScriptableObject
{
    public BaseAsset baseAsset;
    public List<CardAsset> cards = new List<CardAsset>();
    public List<CardAsset> buildings = new List<CardAsset>();
}
