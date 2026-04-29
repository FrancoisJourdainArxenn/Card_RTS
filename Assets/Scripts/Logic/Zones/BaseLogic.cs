using System.Collections;
using System.Collections.Generic;
using System;

[System.Serializable]
public class BaseLogic: ILivable
{
    public Player owner;
    public BaseAsset ba;
    public NeutralZoneController neutralBaseController;
    //public BuildingEffect effect;
    private int uniqueBaseID;

    public int ID => uniqueBaseID;
    public ZoneLogic Zone => neutralBaseController.zone.Logic;
       
    private int baseHealth; // the basic health that we have in BaseAsset
    public int MaxHealth // health with all the current buffs taken into account
    {
        get{ return baseHealth;}
    }

    private int health; // current health of this base
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

    public int BaseID {get; private set;}
    
    public void Die()
    {
        owner.controlledBaseAssets.Remove(ba);
        owner.CalculatePlayerIncome();
        BasesCreatedThisGame.Remove(uniqueBaseID);
        FogOfWarManager.Refresh();
        new BaseDieCommand(uniqueBaseID, neutralBaseController).AddToQueue();
    }

    public BaseLogic(Player owner, BaseAsset ba, NeutralZoneController neutralBaseController, int networkID = -1)
    {
        this.ba = ba;
        this.neutralBaseController = neutralBaseController;
        baseHealth = ba.MaxHealth;
        Health = baseHealth;
        baseMainRessourceIncome = ba.mainRessourceIncome;
        baseSecondRessourceIncome = ba.secondRessourceIncome;
        this.owner = owner;
        uniqueBaseID = networkID >= 0 ? networkID : IDFactory.GetUniqueID();
        BasesCreatedThisGame.Add(uniqueBaseID, this);
        FogOfWarManager.Refresh();
    }

    // STATIC For managing IDs
    public static Dictionary<int, BaseLogic> BasesCreatedThisGame = new Dictionary<int, BaseLogic>();
}
