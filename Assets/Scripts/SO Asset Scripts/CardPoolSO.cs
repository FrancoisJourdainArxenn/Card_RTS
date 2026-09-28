using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CardPoolSO", menuName = "Card RTS/Card Pool")]
public class CardPoolSO : ScriptableObject
{
    public BaseAsset baseAsset;
    // Si assignée, remplace la "base principale" statique par une unité mobile : mêmes règles
    // d'income/tiers que baseAsset (voir Player.HomeUnits), mais c'est une CreatureLogic normale
    // (déplacement, attaque, capacités) et ce joueur n'est vaincu qu'une fois toutes ses
    // unités-bases tuées (celle-ci et toute carte CountsAsHomeBase jouée ensuite), où qu'elles
    // soient sur la carte. Laisser vide pour garder une base classique immobile.
    public CardAsset homeUnit;

    [Header("Card Pool Info")]
    public string poolName;
    public Sprite cardPoolIcon;

    public List<CardAsset> cards = new List<CardAsset>();
}
