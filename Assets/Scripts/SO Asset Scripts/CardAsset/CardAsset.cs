using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public enum CardType
{
    Unit,
    Building,
    Action,
}

public enum SubType
{
    //Units
    Soldier =0,
    Swarm =1,
    Robot =2,
    Mechanical =3,
    Organic =5,
    Engineer =6,
    Pirate =7,
    Mystic =9,

    //Action
    Spell = 4,
    Order = 8,
    Invention = 10,
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
    [InspectorName("3")] T3 = 3,
    [InspectorName("4")] T4 = 4,
    [InspectorName("5")] T5 = 5
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
    public float AttackSpeedMultiplier = 2f;
    public List<AttackModifierSO> AttackModifiers = new List<AttackModifierSO>();
   

    [Header("Keywords")]
    // Autorise le joueur à poser des créatures depuis sa main dans la zone où se trouve
    // cette créature, même sans y contrôler de base. Perdu si la créature meurt ou change de zone.
    public bool Commandement = false;
    // Autorise à poser CETTE carte dans une zone où le joueur a déjà une unité, même sans
    // Commandement et sans contrôler de base là-bas. Vérifié une seule fois, au moment de la pose.
    public bool Renfort = false;
    public int MoveSpeed = 1;
    public bool Celerity = false;
    public bool melee = false;
    // Immobile, ne peut jamais attaquer (attaque verrouillée à 0), immunisée aux buffs de type
    // bouclier/célérité — occupe quand même une place en rangée Melee/Ranged et reste ciblable
    // normalement (dégâts, soins) comme n'importe quelle autre unité.
    public bool IsStructureUnit = false;




    [Header("Global Properties")]
    public List<Keyword> Keywords = new List<Keyword>();
    public List<CardAsset> ReferencedCards = new List<CardAsset>();
    [Header("Effects")]
    public List<CardEffectData> Effects = new List<CardEffectData>();

    // Description avec placeholders {0}, {1}... résolus par le premier effet qui expose des valeurs
    // substituables (voir EffectSO.GetDescriptionValues) — reflète les bonus d'amplificateurs actuels
    // de viewer. Une carte sans placeholder (donc sans effet substituable trouvé) garde son texte tel quel.
    public string GetResolvedDescription(Player viewer)
    {
        if (Effects == null) return Description;
        foreach (CardEffectData data in Effects)
        {
            if (data.Effect == null) continue;
            object[] values = data.Effect.GetDescriptionValues(viewer, this);
            if (values != null) return string.Format(Description, values);
        }
        return Description;
    }
}
