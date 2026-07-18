using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public enum CardType
{
    Unit,
    Building,
    Order,
}

public enum SubType
{
    Soldier,
    Swarm,
    Robot,
    Mechanical,
    Building,
}

public enum TargetingOptions
{
    NoTarget,
    AllCreatures, 
    EnemyCreatures,
    YourCreatures, 
    AllCharacters, 
    EnemyCharacters,
    YourCharacters
}

public enum CardTier
{
    [InspectorName("1")] T1 = 1,
    [InspectorName("2")] T2 = 2,
    [InspectorName("3")] T3 = 3
}

public class CardAsset : ScriptableObject
{
    // this object will hold the info about the most general card
    [Header("General info")]
    public string Name;
    public FactionAsset Faction;  // if this is null, it`s a neutral card
    public CardType Type;
    public SubType subType;
    public CardTier tier = CardTier.T1;
    [TextArea(2,3)]
    public string Description;  // Description for spell or character
	public Sprite CardImage;
    public int MainCost;

    [Header("Hero")]
    public bool IsHero = false;
    public HeroUnlockConditionSO UnlockCondition;

    [Header("Attack and Health info")]
    public int Attack;
    public int MaxHealth;   // =0 => spell card
    public int AttacksForOneTurn = 1;
    public bool melee = false;
    public float AttackSpeedMultiplier = 1f;
    public List<AttackModifierSO> AttackModifiers = new List<AttackModifierSO>();
   
    [Header("Movement info")]
    public int MoveSpeed = 1;
    public bool Celerity = false;
    
    [Header("Global Properties")]
    public int ActivationsForOneTurn = 0;
    [Header("Effects")]
    public List<Keyword> Keywords = new List<Keyword>();
    public List<CardEffectData> Effects = new List<CardEffectData>();
}
