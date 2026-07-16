using UnityEngine;
using System.Collections.Generic;
using TMPro;

[CreateAssetMenu(fileName = "BaseAsset", menuName = "BaseAsset")]
public class BaseAsset : ScriptableObject
{
    [Header("General info")]
    public FactionAsset Faction;
    public Sprite BaseImage;
    public string BaseName;
    public int MaxHealth;
    public int mainRessourceIncome;
    public int mainRessourceBaseCost;

    [Header("Upgrade tiers")]
    public List<BaseTierLevel> tierLevels = new List<BaseTierLevel>();
}

[System.Serializable]
public class BaseTierLevel
{
    public CardTier tier;
    [Tooltip("Revenu additionnel accordé une fois ce tier atteint")]
    public int incomeBonus;
    [Tooltip("Coût initial pour atteindre ce tier depuis le précédent")]
    public int upgradeCost;
    [Tooltip("Coût minimum après réduction passive")]
    public int upgradeCostFloor;
    [Tooltip("Réduction du coût à chaque début de tour")]
    public int upgradeCostReductionPerTurn;
    [Tooltip("Config de tirage pondéré active une fois ce tier atteint")]
    public WeightedDrawConfig drawConfig;
    [Tooltip("Icône représentant ce tier (T1/T2/T3)")]
    public Sprite tierIcon;
}
