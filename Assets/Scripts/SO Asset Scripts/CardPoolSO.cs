using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CardPoolSO", menuName = "Card RTS/Card Pool")]
public class CardPoolSO : ScriptableObject
{
    public BaseAsset baseAsset;
    // Si assignée, remplace la "base principale" statique par une unité mobile : mêmes règles
    // d'income/tiers que baseAsset (voir Player.HomeUnit), mais c'est une CreatureLogic normale
    // (déplacement, attaque, capacités) et le seul moyen de vaincre ce joueur est de la tuer, où
    // qu'elle soit sur la carte. Laisser vide pour garder une base classique immobile.
    public CardAsset homeUnit;
    public List<CardAsset> cards = new List<CardAsset>();
    public List<CardAsset> buildings = new List<CardAsset>();
}
