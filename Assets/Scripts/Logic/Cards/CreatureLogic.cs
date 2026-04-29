using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class CreatureLogic: ILivable 
{
    // PUBLIC FIELDS
    public Player owner;
    public CardAsset ca;
    public int UniqueCreatureID;

    // PROPERTIES
    // property from ILivable interface
    public int ID
    {
        get{ return UniqueCreatureID; }
    }

    public int BaseID {get; private set;}
    // the basic health that we have in CardAsset
    private int baseHealth;
    // health with all the current buffs taken into account
    public int MaxHealth
    {
        get{ return baseHealth;}
    }

    // current health of this creature
    private int health;
    public int Health
    {
        get{ return health; }

        set
        {
            if (value > MaxHealth)
                health = MaxHealth;
            else if (value <= 0)
                Die();
            else
                health = value;
        }
    }

    // returns true if we can attack with this creature now
    public bool CanAttack
    {
        get
        {
            bool battlePhase = TurnManager.Instance != null && TurnManager.Instance.IsBattlePhase;
            bool ownersTurn = battlePhase && TurnManager.Instance.MayPlayerUseControlsInPhase(owner);
            return ownersTurn && (AttacksLeftThisTurn > 0);
        }
    }

    // returns true if we can move with this creature now
    public bool CanMove
    {
        get
        {
            bool commandPhase = TurnManager.Instance != null && TurnManager.Instance.IsCommandPhase;
            bool ownersTurn = commandPhase && TurnManager.Instance.MayPlayerUseControlsInPhase(owner);
            return ownersTurn && (MovementsLeftThisTurn > 0);
        }
    }

    // property for Attack
    private int baseAttack;
    public int Attack
    {
        get{ return baseAttack; }
    }
     
    // number of attacks for one turn if (attacksForOneTurn==2) => Windfury
    private int attacksForOneTurn = 1;
    public int AttacksLeftThisTurn
    {
        get;
        set;
    }

    // number of movements for one turn if (movementsForOneTurn==2) => Celerity
    private int movementsForOneTurn = 1;
    public int MovementsLeftThisTurn
    {
        get;
        set;
    }

    public ZoneLogic Zone => owner.GetPlayerAreaByID(BaseID)?.parentZone.Logic;

    public bool IsMelee
    {
        get { return ca.melee; }
    }

    public bool Targetable
    {
        get
        {
            if(IsMelee)
                return true;
            foreach (CreatureLogic creatureLogic in owner.playedCards.Creatures)
            {
                if (
                    creatureLogic.IsMelee 
                    && creatureLogic.UniqueCreatureID != UniqueCreatureID 
                    && creatureLogic.BaseID == BaseID
                    && ZoneCombatResolver.WouldSurvive(creatureLogic)
                ) {
                    return false;
                }
            }
            return true;
        }
    }

    // CONSTRUCTOR
    // networkID : si >= 0, utilise cet ID (fourni par le serveur) au lieu d'en générer un nouveau.
    // En mode local, laisser networkID à -1 (valeur par défaut).
    public CreatureLogic(Player owner, CardAsset ca, int baseID, int networkID = -1)
    {
        this.ca = ca;
        baseHealth = ca.MaxHealth;
        Health = ca.MaxHealth;
        baseAttack = ca.Attack;
        attacksForOneTurn = ca.AttacksForOneTurn;
        movementsForOneTurn = ca.MoveSpeed;
        // AttacksLeftThisTurn is now equal to 0
        //if (ca.Charge)
        //  AttacksLeftThisTurn = attacksForOneTurn;
        if (ca.Celerity)
            MovementsLeftThisTurn = movementsForOneTurn;
        this.owner = owner;
        this.BaseID = baseID;
        UniqueCreatureID = networkID >= 0 ? networkID : IDFactory.GetUniqueID();
        if (ca.Effects != null && ca.Effects.Count > 0)
            EffectProcessor.RegisterCreatureEffects(this, ca);
        CreaturesCreatedThisGame.Add(UniqueCreatureID, this);
    }

    // METHODS
    public void OnTurnStart()
    {
        AttacksLeftThisTurn = attacksForOneTurn;
        MovementsLeftThisTurn = movementsForOneTurn;
        Debug.Log("Movements Left This Turn: " + MovementsLeftThisTurn);
    }

    public void ApplyBuff(int attackDelta, int healthDelta)
    {
        baseAttack += attackDelta;
        baseHealth += healthDelta;
        if (healthDelta > 0) health += healthDelta;
    }

    public void Die()
    {   
        owner.playedCards.Creatures.Remove(this);
        
        // cause Deathrattle Effect
        EffectProcessor.NotifyCreatureDied(this, owner);
        
        FogOfWarManager.Refresh();
        new CreatureDieCommand(UniqueCreatureID, owner).AddToQueue();
    }

    public void GoFace()
    {
        AttacksLeftThisTurn--;
        int targetHealthAfter = owner.otherPlayer.Health - Attack;
        new CreatureAttackCommand(owner.otherPlayer.PlayerID, UniqueCreatureID, 0, Attack, Health, targetHealthAfter).AddToQueue();
        owner.otherPlayer.Health -= Attack;
    }

    public void AttackCreature (CreatureLogic target)
    {
        AttacksLeftThisTurn--;
        // calculate the values so that the creature does not fire the DIE command before the Attack command is sent
        int targetHealthAfter = target.Health - Attack;
        int attackerHealthAfter = Health - target.Attack;
        new CreatureAttackCommand(target.UniqueCreatureID, UniqueCreatureID, target.Attack, Attack, attackerHealthAfter, targetHealthAfter).AddToQueue();

        target.Health -= Attack;
        Health -= target.Attack;
    }

    public void AttackCreatureWithID(int uniqueCreatureID)
    {
        CreatureLogic target = CreatureLogic.CreaturesCreatedThisGame[uniqueCreatureID];
        AttackCreature(target);
    }

    public void AttackBaseWithID(int uniqueBaseID)
    {
        BaseLogic target = BaseLogic.BasesCreatedThisGame[uniqueBaseID];
        AttackBase(target);
    }

    public void AttackBase(BaseLogic target)
    {
        AttacksLeftThisTurn--;
        int targetHealthAfter = target.Health - Attack;
        new CreatureAttackCommand(target.ID, UniqueCreatureID, 0, Attack, Health, targetHealthAfter).AddToQueue();
        target.Health -= Attack;

    }

    public void Move(int baseID, int tablePos)
    {
        MovementsLeftThisTurn--;
        BaseID = baseID;
        FogOfWarManager.Refresh();
        new CreatureMoveCommand(UniqueCreatureID, baseID, tablePos).AddToQueue();
    }

    // STATIC For managing IDs
    public static Dictionary<int, CreatureLogic> CreaturesCreatedThisGame = new Dictionary<int, CreatureLogic>();


}
