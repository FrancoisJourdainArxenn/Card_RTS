using UnityEngine;
using System.Collections.Generic;
using TMPro;

[CreateAssetMenu(fileName = "BaseAsset", menuName = "BaseAsset")]
public class BaseAsset : ScriptableObject
{
    [Header("General info")]
    public FactionAsset Faction;
    public Sprite BaseImage;
    public Sprite BaseIcon;
    public Color baseIconColor;
    [TextArea(3, 10)] public string baseDescription;

    public string BaseName;
    public int MaxHealth;
    public int mainRessourceIncome;
    public int mainRessourceBaseCost;

    [Header("Upgrade tiers")]
    public List<BaseTierLevel> tierLevels = new List<BaseTierLevel>();

    [Header("Keywords")]
    public bool HasScout = false;
    public CardTier scoutMinTier = CardTier.T1;
    public bool IsTeleporter = false;
    public CardTier teleporterMinTier = CardTier.T1;

    [Header("Row Capacity")]
    public bool ModifiesRowCapacity = false;
    public int RowCapacityBonus = 0;
    public bool RowCapacityBonusIsGlobal = false;
    public CardTier rowCapacityMinTier = CardTier.T1;

    [Header("Global Properties")]
    public List<Keyword> Keywords = new List<Keyword>();

    [Header("Effects")]
    public List<CardEffectData> Effects = new List<CardEffectData>();
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
    [Tooltip("Cartes piochées en plus par tour, une fois ce tier atteint")]
    public int drawCountBonus;
    [Tooltip("Icône représentant ce tier (T1/T2/T3)")]
    public Sprite tierIcon;
}
