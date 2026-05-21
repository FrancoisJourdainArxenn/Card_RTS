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
    public int ID => UniqueCreatureID;
    public string DisplayName => ca.name;

    public int BaseID {get; private set;}
    private readonly int baseHealth;
    private int permMaxHealthBonus;
    private int tempMaxHealthBonus;
    public int MaxHealth
    {
        get => baseHealth + permMaxHealthBonus + tempMaxHealthBonus;
        set => permMaxHealthBonus = value - baseHealth - tempMaxHealthBonus;
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
            {
                if (IsPendingDeath) return;

                bool inCombatPhase = TurnManager.Instance != null && (
                    TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.BeginCombat ||
                    TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Battle ||
                    TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.End
                );

                if (inCombatPhase)
                {
                    health = 0;
                    IsPendingDeath = true;
                    PendingDeathList.Add(this);
                    GameObject go = IDHolder.GetGameObjectWithID(UniqueCreatureID);
                    go?.GetComponent<VfxManager>()?.ShowDeathPending();
                }
                else
                {
                    Die();
                }
            }
            else
                health = value;
        }
    }

    public bool IsPendingDeath { get; private set; }
    public static List<CreatureLogic> PendingDeathList = new List<CreatureLogic>();

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

    private readonly int baseAttack;
    private int permAttackBonus;
    private int tempAttackBonus;
    public int Attack
    {
        get => baseAttack + permAttackBonus + tempAttackBonus;
        set => permAttackBonus = value - baseAttack - tempAttackBonus;
    }
     
    // number of attacks for one turn if (attacksForOneTurn==2) => Windfury
    private int attacksForOneTurn = 1;
    public int AttacksLeftThisTurn { get; set; }

    // number of movements for one turn if (movementsForOneTurn==2) => Celerity
    private int movementsForOneTurn = 1;
    public int MovementsLeftThisTurn { get; set; }

    public ZoneLogic Zone => owner.GetPlayerAreaByID(BaseID)?.parentZone.Logic;

    public bool IsMelee => ca.melee;
    public bool IsRanged => !ca.melee;
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
        Attack = baseAttack;
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
        CreaturesCreatedThisGame.Add(UniqueCreatureID, this);
        if (ca.Effects != null && ca.Effects.Count > 0)
            EffectRegistry.RegisterCreatureEffects(this, ca);
    }

    // METHODS
    public void OnTurnStart()
    {
        AttacksLeftThisTurn = attacksForOneTurn;
        MovementsLeftThisTurn = movementsForOneTurn;
    }

    public void ApplyBuff(int attackDelta, int healthDelta)
    {
        permAttackBonus += attackDelta;
        permMaxHealthBonus += healthDelta;
        if (healthDelta > 0) health += healthDelta;
    }

    public void Die()
    {
        bool wasInList = owner.playedCards.Creatures.Remove(this);
        EffectRegistry.NotifyCreatureDied(this, owner);
        FogOfWarManager.Refresh();
        if (wasInList)
            new CreatureDieCommand(UniqueCreatureID, owner).AddToQueue();
    }

    // During Battle: queues the visual die command immediately so the creature disappears
    // during the attack animation. Deathrattle fires later via DrainPendingDeaths at End phase.
    public void ScheduleBattleDeath()
    {
        if (IsPendingDeath) return;
        health = 0;
        IsPendingDeath = true;
        PendingDeathList.Add(this);
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

    public static void ProcessPendingDeaths()
    {
        List<CreatureLogic> toProcess = new (PendingDeathList);
        PendingDeathList.Clear();
        foreach (CreatureLogic creature in toProcess)
            creature.Die();
    }

    // STATIC For managing IDs
    public static Dictionary<int, CreatureLogic> CreaturesCreatedThisGame = new Dictionary<int, CreatureLogic>();


}
