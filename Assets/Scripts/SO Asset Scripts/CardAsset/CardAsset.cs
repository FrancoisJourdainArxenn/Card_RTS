using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public enum CardType
{
    Unit,
    Building,
    Spell
}

public enum SubType
{
    Soldier,
    Swarm,
    Robot,
    Mechanical,
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

public class CardAsset : ScriptableObject 
{
    // this object will hold the info about the most general card
    [Header("General info")]
    public FactionAsset Faction;  // if this is null, it`s a neutral card
    public CardType Type;
    public SubType subType;
    [TextArea(2,3)]
    public string Description;  // Description for spell or character
	public Sprite CardImage;
    public int MainCost, SecondCost;

    [Header("Attack and Health info")]
    public int Attack;
    public int MaxHealth;   // =0 => spell card
    public int AttacksForOneTurn = 1;
    public bool melee = false;
    
    [Header("Movement info")]
    public int MoveSpeed = 1;
    public bool Celerity = false;
    
    [Header("Effects")]
    public List<Keyword> Keywords = new List<Keyword>();
    public TargetingOptions Targets;
    public int ActivationsForOneTurn = 0;
    public List<CardEffectData> Effects = new List<CardEffectData>();
}
