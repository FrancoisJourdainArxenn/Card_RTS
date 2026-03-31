using UnityEngine;
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
    public int secondRessourceIncome;
    public int mainRessourceBuildingCost;
    public int secondRessourceBuildingCost;



}
