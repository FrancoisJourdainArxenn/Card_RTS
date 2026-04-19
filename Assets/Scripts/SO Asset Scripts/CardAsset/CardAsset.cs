using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    [TextArea(2,3)]
    public string Description;  // Description for spell or character
	public Sprite CardImage;
    public int MainCost, SecondCost;

    [Header("Creature Info")]
    public int MaxHealth;   // =0 => spell card
    public int Attack;
    public int AttacksForOneTurn = 1;
    public int MoveSpeed = 1;
    public bool melee = false;
    // public bool Charge = false;
    public bool Celerity = false;
    public string CreatureScriptName;
    public int specialCreatureAmount;
    

    [Header("SpellInfo")]
    public string SpellScriptName;
    public int specialSpellAmount;
    public TargetingOptions Targets;

}
