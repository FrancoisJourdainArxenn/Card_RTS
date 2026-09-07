using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

[System.Serializable]
public class BaseLogic: ILivable
{
    public Player owner;
    public BaseAsset ba;
    public NeutralZoneController neutralBaseController;
    private int uniqueBaseID;

    public bool IsHomeBase => neutralBaseController == null;

    public int ID => uniqueBaseID;
    public string DisplayName => ba.name;
    // Lu en direct (jamais mis en cache) : owner.MainPArea n'est assigné qu'après la construction de
    // homeBaseLogic (GlobalSettings.InitFromMap tourne après Player.Awake), donc un _homeZone figé à
    // la construction restait null pour toute la partie et cassait IsUnderAttack (malus "-1 ressource"
    // jamais appliqué même avec des créatures ennemies dans la home zone).
    // Quand owner.HomeUnit est assignée (base principale = unité mobile, voir Player.HomeUnit), la
    // zone suit l'unité au lieu de rester figée sur MainPArea — IsUnderAttack/EffectiveIncome (dérivés
    // de Zone ci-dessous) s'appliquent alors automatiquement là où l'unité se trouve réellement.
    public ZoneLogic Zone => IsHomeBase
        ? (owner.HomeUnit != null ? owner.HomeUnit.Zone : owner.MainPArea?.parentZone?.Logic)
        : neutralBaseController?.zone?.Logic;

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
                owner.Health = value; // délègue au Player — le game-over est décidé par
                                      // ZoneCombatResolver.ComputeRoundOutcome(), pas par ce setter
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

    public bool IsUnderAttack
    {
        get
        {
            bool result = Zone != null && owner.otherPlayer.Creatures.Exists(c => c.Zone == Zone);
            string role = !NetworkSessionData.IsNetworkSession ? "" : Unity.Netcode.NetworkManager.Singleton.IsServer ? "[Server]" : "[Client]";
            Debug.Log($"[UnderAttack]{role} {owner.name} home zone={(Zone != null ? Zone.ID.ToString() : "null")} -> {result} | enemy creatures: " +
                string.Join(", ", owner.otherPlayer.Creatures.Select(c => $"{c.DisplayName}(base={c.BaseID}, zone={(c.Zone != null ? c.Zone.ID.ToString() : "null")})")));
            return result;
        }
    }
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

    // Nettoyage logique immédiat uniquement — ne met plus en file BaseDieCommand elle-même.
    // L'appelant (ZoneCombatResolver.EnqueueBattleCommands) capture (ID, neutralBaseController) avant
    // d'appeler Die(), et enfile BaseDieCommand une seule fois, après avoir fini de traiter toute la
    // zone (voir diedNeutralBases) — jamais immédiatement ici, sinon l'animation de mort jouerait en
    // plein milieu des autres combats de la même zone plutôt qu'après leur fin.
    // BasesCreatedThisGame.Remove DOIT rester synchrone/immédiat : c'est ce qui fait échouer la
    // recherche (TryGetValue) d'un step ULTÉRIEUR de la même zone visant cette même base déjà morte
    // (voir ZoneCombatResolver, garde en tête du cas TargetKind.Base) — sans quoi un round sans limite
    // anti-overkill (comme pour la base principale) redéclencherait Die() plusieurs fois.
    public void Die()
    {
        if (IsHomeBase) return; // mort de la home base gérée par Player
        owner.controlledBaseAssets.Remove(ba);
        owner.CalculatePlayerIncome();
        BasesCreatedThisGame.Remove(uniqueBaseID);
        FogOfWarManager.Refresh();
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
    public BaseLogic(Player owner)
    {
        this.ba = owner.baseAsset;
        CurrentUpgradeCost = NextTierData?.upgradeCost ?? 0;
        this.neutralBaseController = null;
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

    public void ReduceUpgradeCost(int amount)
    {
        if (!IsHomeBase) return;
        BaseTierLevel next = NextTierData;
        if (next == null) return; // déjà au tier max
        CurrentUpgradeCost = Math.Max(next.upgradeCostFloor, CurrentUpgradeCost - amount);
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
