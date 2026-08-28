using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

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
    public int MaxHealth
    {
        get => baseHealth;
        set => baseHealth = value;
    }
    
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

    public bool IsUnderAttack => Zone != null && owner.otherPlayer.Creatures.Exists(c => c.Zone == Zone);
    public int EffectiveIncome => IsUnderAttack ? Mathf.Max(0, MainRessourceIncome - 1) : MainRessourceIncome;

    public int BaseID {get; private set;}
    public CardTier CurrentTier { get; private set; } = CardTier.T1;
    public int CurrentUpgradeCost { get; private set; }
    public static event Action<BaseLogic> OnUpgradeCostChanged;
    public BaseTierLevel NextTierData =>
        (int)CurrentTier < ba.tierLevels.Count ? ba.tierLevels[(int)CurrentTier] : null;
    public bool IsMaxTier => NextTierData == null;
    public Sprite CurrentTierIcon => ba.tierLevels[(int)CurrentTier - 1].tierIcon;
    public Sprite NextTierIcon => NextTierData?.tierIcon;
    
    public int TakeDamage(int dmg)
    {
        Health -= dmg;
        return Health;
    }

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
        CurrentUpgradeCost = NextTierData?.upgradeCost ?? 0;
        this.neutralBaseController = neutralBaseController;
        baseHealth = ba.MaxHealth;
        health = baseHealth;
        baseMainRessourceIncome = ba.mainRessourceIncome;
        this.owner = owner;
        uniqueBaseID = networkID >= 0 ? networkID : IDFactory.GetUniqueID();
        BasesCreatedThisGame.Add(uniqueBaseID, this);
        FogOfWarManager.Refresh();
    }

    // Constructeur pour les home bases des joueurs
    public BaseLogic(Player owner, ZoneLogic homeZone)
    {
        this.ba = owner.baseAsset;
        CurrentUpgradeCost = NextTierData?.upgradeCost ?? 0;
        this.neutralBaseController = null;
        this._homeZone = homeZone;
        baseHealth = ba.MaxHealth;
        health = baseHealth;
        baseMainRessourceIncome = ba.mainRessourceIncome;
        this.owner = owner;
        uniqueBaseID = owner.PlayerID;
        BasesCreatedThisGame[uniqueBaseID] = this;
    }

    public void TickUpgradeCostDown()
    {
        if (!IsHomeBase) return;
        BaseTierLevel next = NextTierData;
        if (next == null) return; // déjà au tier max
        CurrentUpgradeCost = Math.Max(next.upgradeCostFloor, CurrentUpgradeCost - next.upgradeCostReductionPerTurn);
        OnUpgradeCostChanged?.Invoke(this);
    }

    public bool TryUpgrade()
    {
        if (!IsHomeBase) return false;
        BaseTierLevel next = NextTierData;
        if (next == null) return false;
        if (owner.MainRessourceAvailable < CurrentUpgradeCost) return false;

        owner.MainRessourceAvailable -= CurrentUpgradeCost;
        owner.AddBonusIncomeFromSource(ID, next.incomeBonus);
        owner.AddBonusHandDrawCountFromSource(ID, next.drawCountBonus);
        owner.deck.drawConfig = next.drawConfig;

        CurrentTier = next.tier;
        CurrentUpgradeCost = NextTierData?.upgradeCost ?? 0;
        OnUpgradeCostChanged?.Invoke(this);
        return true;
    }

    // STATIC For managing IDs
    public static Dictionary<int, BaseLogic> BasesCreatedThisGame = new Dictionary<int, BaseLogic>();
}
