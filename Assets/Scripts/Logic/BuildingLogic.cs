using System.Collections;
using System.Collections.Generic;
using System;

[System.Serializable]
public class BuildingLogic: ILivable
{
    public Player owner;
    public BaseAsset ba;
    public NeutralBaseController neutralBaseController;
    //public BuildingEffect effect;
    public int UniqueBuildingID;

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
        
        new BuildingDieCommand(UniqueBuildingID, neutralBaseController).AddToQueue();
    }

    public BuildingLogic(Player owner, BaseAsset ba, NeutralBaseController neutralBaseController)
    {
        this.ba = ba;
        this.neutralBaseController = neutralBaseController;
        baseHealth = ba.MaxHealth;
        Health = baseHealth;
        baseMainRessourceIncome = ba.mainRessourceIncome;
        baseSecondRessourceIncome = ba.secondRessourceIncome;
        this.owner = owner;
        UniqueBuildingID = IDFactory.GetUniqueID();
        BuildingsCreatedThisGame.Add(UniqueBuildingID, this);
    }

    // STATIC For managing IDs
    public static Dictionary<int, BuildingLogic> BuildingsCreatedThisGame = new Dictionary<int, BuildingLogic>();
}
