using System.Collections;
using System.Collections.Generic;
using System;

[System.Serializable]
public class BuildingLogic: ILivable
{
    public Player owner;
    public BaseAsset ba;
    public OneBuildingManager buildingManager;
    //public BuildingEffect effect;
    public int UniqueBuildingID;
    public bool Frozen = false;

    public int ID
    {
        get{ return UniqueBuildingID; }
    }
       
    private int baseHealth; // the basic health that we have in BaseAsset
    public int MaxHealth // health with all the current buffs taken into account
    {
        get{ return baseHealth;}
    }

    private int health; // current health of this building
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

    private int baseMainRessourceIncome;
    public int MainRessourceIncome
    {
        get{ return baseMainRessourceIncome; }
    }
    
    private int baseSecondRessourceIncome;
    public int SecondRessourceIncome
    {
        get{ return baseSecondRessourceIncome; }
    }
    
    public void Die()
    {
        owner.controlledBases.Remove(ba);
        owner.CalculatePlayerIncome();
        BuildingsCreatedThisGame.Remove(UniqueBuildingID);

        //new BuildingDieCommand(UniqueBuildingID, owner, buildingManager).AddToQueue();
    }

    public BuildingLogic(Player owner, BaseAsset ba)
    {
        this.ba = ba;
        baseHealth = ba.MaxHealth;
        Health = baseHealth;
        baseMainRessourceIncome = ba.mainRessourceIncome;
        baseSecondRessourceIncome = ba.secondRessourceIncome;
        this.owner = owner;
        UniqueBuildingID = IDFactory.GetUniqueID();
        BuildingsCreatedThisGame.Add(UniqueBuildingID, this);
        //buildingManager = gameObject.GetComponent<OneBuildingManager>();
    }

    // STATIC For managing IDs
    public static Dictionary<int, BuildingLogic> BuildingsCreatedThisGame = new Dictionary<int, BuildingLogic>();
}
