using System.Collections.Generic;

[System.Serializable]
public class BuildingLogic : ILivable
{
    public Player owner;
    public CardAsset ca;
    public int UniqueBuildingID;
    public BuildSpotVisual OriginSpot { get; set; }
    public int OriginZoneID { get; private set; }

    public int ID => UniqueBuildingID;

    private int baseHealth;
    public int MaxHealth => baseHealth;

    private int health;
    public int Health
    {
        get => health;
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

    private int baseAttack;
    public int Attack => baseAttack;

    private int attacksForOneTurn = 0;
    private int activationForOneTurn = 0;
    public int AttacksLeftThisTurn { get; set; }
    public int ActivationLeftThisTurn {get; set; }

    public bool IsMelee => ca.melee;

    public bool Targetable => true;

    public bool CanAttack
    {
        get
        {
            bool battlePhase = TurnManager.Instance != null && TurnManager.Instance.IsBattlePhase;
            bool ownersTurn = battlePhase && TurnManager.Instance.MayPlayerUseControlsInPhase(owner);
            return ownersTurn && AttacksLeftThisTurn > 0;
        }
    }

    public BuildingLogic(Player owner, CardAsset ca, BuildSpotVisual originSpot, int networkID = -1)
    {
        this.ca = ca;
        this.owner = owner;
        this.OriginSpot = originSpot;
        this.OriginZoneID = originSpot.Zone.ZoneID;
        baseHealth = ca.MaxHealth;
        health = ca.MaxHealth;
        baseAttack = ca.Attack;
        attacksForOneTurn = ca.AttacksForOneTurn;
        activationForOneTurn = ca.ActivationsForOneTurn;
        UniqueBuildingID = networkID >= 0 ? networkID : IDFactory.GetUniqueID();
        BuildingsCreatedThisGame.Add(UniqueBuildingID, this);
    }

    public void OnTurnStart()
    {
        AttacksLeftThisTurn = attacksForOneTurn;
        ActivationLeftThisTurn = activationForOneTurn;
    }

    public void Die()
    {
        owner.playedCards.Buildings.Remove(this);
        new BuildingDieCommand(UniqueBuildingID).AddToQueue();
    }

    public void AttackCreature(CreatureLogic target)
    {
        AttacksLeftThisTurn--;
        int targetHealthAfter = target.Health - Attack;
        int attackerHealthAfter = Health - target.Attack;
        new BuildingAttackCommand(target.UniqueCreatureID, UniqueBuildingID, target.Attack, Attack, attackerHealthAfter, targetHealthAfter).AddToQueue();
        target.Health -= Attack;
        Health -= target.Attack;
    }

    public void AttackCreatureWithID(int uniqueCreatureID)
    {
        AttackCreature(CreatureLogic.CreaturesCreatedThisGame[uniqueCreatureID]);
    }

    public void AttackBase(BaseLogic target)
    {
        AttacksLeftThisTurn--;
        int targetHealthAfter = target.Health - Attack;
        new BuildingAttackCommand(target.ID, UniqueBuildingID, 0, Attack, Health, targetHealthAfter).AddToQueue();
        target.Health -= Attack;
    }

    public void AttackBaseWithID(int uniqueBaseID)
    {
        AttackBase(BaseLogic.BasesCreatedThisGame[uniqueBaseID]);
    }

    public void AttackBuilding(BuildingLogic target)
    {
        AttacksLeftThisTurn--;
        int targetHealthAfter = target.Health - Attack;
        int attackerHealthAfter = Health - target.Attack;
        new BuildingAttackCommand(target.UniqueBuildingID, UniqueBuildingID, target.Attack, Attack, attackerHealthAfter, targetHealthAfter).AddToQueue();
        target.Health -= Attack;
        Health -= target.Attack;
    }

    public void AttackBuildingWithID(int uniqueBuildingID)
    {
        AttackBuilding(BuildingsCreatedThisGame[uniqueBuildingID]);
    }

    public static Dictionary<int, BuildingLogic> BuildingsCreatedThisGame = new Dictionary<int, BuildingLogic>();
}
