using System.Collections;
using System.Collections.Generic;
using System;

[System.Serializable]
public class BaseLogic: ILivable
{
    public Player owner;
    public BaseAsset ba;
    public NeutralZoneController neutralBaseController;
    private int uniqueBaseID;
    private ZoneLogic _homeZone;

    public bool IsHomeBase => neutralBaseController == null;

    public int ID => uniqueBaseID;
    public string DisplayName => ba.name;
    public ZoneLogic Zone => IsHomeBase ? _homeZone : neutralBaseController?.zone?.Logic;

    private int baseHealth;
    public int MaxHealth => baseHealth;
    
    // TODO clean this
    public int Attack {
        get {
            return 0;
        }
        set {} 
    }
    private int health;
    public int Health
    {
        get => IsHomeBase ? owner.Health : health;
        set
        {
            if (IsHomeBase)
            {
                owner.Health = value; // délègue au Player — Player.Die() gère le game-over
                return;
            }
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
        if (IsHomeBase) return; // mort de la home base gérée par Player
        owner.controlledBaseAssets.Remove(ba);
        owner.CalculatePlayerIncome();
        BasesCreatedThisGame.Remove(uniqueBaseID);
        FogOfWarManager.Refresh();
        new BaseDieCommand(uniqueBaseID, neutralBaseController).AddToQueue();
    }

    // Constructeur pour les bases neutres capturées
    public BaseLogic(Player owner, BaseAsset ba, NeutralZoneController neutralBaseController, int networkID = -1)
    {
        this.ba = ba;
        this.neutralBaseController = neutralBaseController;
        baseHealth = ba.MaxHealth;
        health = baseHealth;
        baseMainRessourceIncome = ba.mainRessourceIncome;
        baseSecondRessourceIncome = ba.secondRessourceIncome;
        this.owner = owner;
        uniqueBaseID = networkID >= 0 ? networkID : IDFactory.GetUniqueID();
        BasesCreatedThisGame.Add(uniqueBaseID, this);
        FogOfWarManager.Refresh();
    }

    // Constructeur pour les home bases des joueurs
    public BaseLogic(Player owner, ZoneLogic homeZone)
    {
        this.ba = owner.baseAsset;
        this.neutralBaseController = null;
        this._homeZone = homeZone;
        baseHealth = ba.MaxHealth;
        health = baseHealth;
        baseMainRessourceIncome = ba.mainRessourceIncome;
        baseSecondRessourceIncome = ba.secondRessourceIncome;
        this.owner = owner;
        uniqueBaseID = owner.PlayerID;
        BasesCreatedThisGame[uniqueBaseID] = this;
    }

    // STATIC For managing IDs
    public static Dictionary<int, BaseLogic> BasesCreatedThisGame = new Dictionary<int, BaseLogic>();
}
