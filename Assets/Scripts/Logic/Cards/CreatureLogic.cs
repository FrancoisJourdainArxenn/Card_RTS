using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

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
                    TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Battle      ||
                    TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.EndBattle
                );

                if (inCombatPhase)
                {
                    MarkPendingDeath();
                    string pendingRole = !NetworkSessionData.IsNetworkSession ? "" : NetworkManager.Singleton.IsServer ? "[Server]" : "[Client]";
                    UnityEngine.Debug.Log($"[Death]{pendingRole} PENDING — {DisplayName} (ID:{UniqueCreatureID}) | PendingDeathList taille: {PendingDeathList.Count}");
                    VfxManager vfx = Vfx;
                    if (vfx != null)
                        Command.DeferAction(() => vfx.ShowDeathPending());
                }
                else
                {
                    Die();
                }
            }
            else
            {
                health = value;
            }
        }
    }

    public bool IsPendingDeath { get; private set; }
    public static List<CreatureLogic> PendingDeathList = new List<CreatureLogic>();

    // True once OnDeath has already been resolved ahead of time during battle planning
    // (see ResolvePredictedBattleDeath). Prevents NotifyCreatureDied from firing OnDeath
    // a second time when the real Die() eventually runs (end of phase / drain).
    public bool OnDeathResolvedInBattle { get; private set; }

    public int ShieldValue { get; private set; } = 0;

    public void ApplyShield(int value)
    {
        ShieldValue = Mathf.Max(ShieldValue, value);
    }

    public int TakeDamage(int dmg)
    {
        if (ShieldValue > 0)
        {
            int absorbed = Mathf.Min(dmg, ShieldValue);
            ShieldValue -= absorbed;
            dmg -= absorbed;
            VfxManager vfx = Vfx;
            if (vfx != null)
            {
                if (ShieldValue == 0)
                    vfx.HideShieldVfx();
                else
                    vfx.UpdateShieldVfx(ShieldValue);
            }
        }
        Health -= dmg;
        return Health;
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
    public List<AttackModifierSO> AttackModifiers => ca.AttackModifiers;
    public float AttackSpeedMultiplier => ca.AttackSpeedMultiplier;




    private VfxManager Vfx
    {
        get
        {
            GameObject go = IDHolder.GetGameObjectWithID(UniqueCreatureID);
            return go != null ? go.GetComponent<VfxManager>() : null;
        }
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
        owner.matchStats.Add(MatchStatType.UnitsSummoned);
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
        string dieRole = !NetworkSessionData.IsNetworkSession ? "" : NetworkManager.Singleton.IsServer ? "[Server]" : "[Client]";
        UnityEngine.Debug.Log($"[Death]{dieRole} DIE — {DisplayName} (ID:{UniqueCreatureID}) | était dans playedCards: {owner.playedCards.Creatures.Contains(this)}");
        bool wasInList = owner.playedCards.Creatures.Remove(this);
        EffectRegistry.NotifyCreatureDied(this, owner);
        DeathDrainRecorder.RecordDeath(UniqueCreatureID);
        if (wasInList)
            new CreatureDieCommand(UniqueCreatureID, owner).AddToQueue();

        if (ca.IsHero)
            ReturnHeroToHand();
    }

    // A hero that dies comes back to its owner's hand, locked again.
    // Die() only runs server-side in network sessions (clients replay via SilentDie), so the
    // network case must be broadcast rather than applied directly to avoid only updating the host.
    private void ReturnHeroToHand()
    {
        if (NetworkSessionData.IsNetworkSession)
            GameNetworkManager.Instance.BroadcastHeroReturnToHand(owner.playerIndex);
        else
        {
            ca.UnlockCondition?.ResetProgress(owner);
            owner.GetACardNotFromDeck(ca);
        }
    }

    // Meurt silencieusement sans déclencher d'effets OnDeath.
    // Utilisé côté client pour rejouer le résultat du drain serveur.
    public void SilentDie()
    {
        Debug.Log($"[Death][Client] SILENT_DIE — {DisplayName} (ID:{UniqueCreatureID})");
        bool wasInList = owner.playedCards.Creatures.Remove(this);
        TempEffectTracker.Unregister(UniqueCreatureID);
        EffectRegistry.UnregisterEntity(UniqueCreatureID);
        if (wasInList)
            new CreatureDieCommand(UniqueCreatureID, owner).AddToQueue();
    }

    // During Battle: queues the visual die command immediately so the creature disappears
    // during the attack animation. Deathrattle fires later via DrainPendingDeaths at End phase.
    public void ScheduleBattleDeath()
    {
        if (IsPendingDeath) return;
        MarkPendingDeath();
    }

    // Marque cette créature comme mourante pendant le combat : figé une seule fois (voir garde
    // IsPendingDeath des deux appelants), aussi bien depuis ScheduleBattleDeath (mort via une
    // BattleStepRecord planifiée) que depuis le setter de Health (mort via des dégâts directs,
    // ex: un effet OnDeath comme "Acid Explosion" qui tue une autre créature pendant la
    // planification). Les deux chemins doivent enqueue la même CreatureDieCommand et flush les
    // mêmes commandes différées — sans ça, une mort par dégâts directs ne joue jamais visuellement
    // et laisse d'éventuelles commandes différées orphelines dans Command._deferredBySource.
    private void MarkPendingDeath()
    {
        health = 0;
        IsPendingDeath = true;
        PendingDeathList.Add(this);
        new CreatureDieCommand(UniqueCreatureID, owner).AddToQueue();
        Command.FlushDeferredCommands(UniqueCreatureID);
    }

    // Résout OnDeath immédiatement, à l'instant où ZoneCombatResolver prédit cette mort
    // pendant la planification du combat (avant qu'aucune attaque suivante de la séquence
    // ne soit assignée) — pas d'attente de la fin de la phase Battle. Permet à un effet du
    // type "OnDeath: allié +1/+1" d'affecter les dégâts et la survie du reste du combat.
    // Ne touche ni Health/IsPendingDeath/PendingDeathList (toujours gérés par
    // ScheduleBattleDeath/Die en Phase B, inchangés) : seule l'exécution de l'effet avance.
    public void ResolvePredictedBattleDeath()
    {
        if (OnDeathResolvedInBattle) return;
        OnDeathResolvedInBattle = true;

        if (ca.Effects == null) return;

        bool isNetworkServer = NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer;
        for (int i = 0; i < ca.Effects.Count; i++)
        {
            CardEffectData data = ca.Effects[i];
            if (data.Trigger != TriggerType.OnDeath) continue;

            // try/catch : une exception ici (ex: un effet OnDeath mal configuré) ne doit ni
            // interrompre la planification du combat pour le reste de la partie, ni — surtout —
            // laisser Command.DeferForBattleReplay/EffectSO._networkRng bloqués à leur valeur
            // "en cours" : ce sont des flags statiques globaux, donc s'ils ne sont jamais remis à
            // leur état normal, TOUTE commande visuelle du jeu (attaques, morts, pioches...) se
            // retrouve reportée indéfiniment nulle part — symptôme observé : "le combat bloque"
            // après la mort d'une créature dont l'effet OnDeath a levé une exception.
            try
            {
                if (!NetworkSessionData.IsNetworkSession)
                {
                    Command.RunDeferred(UniqueCreatureID, () =>
                        EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                }
                else if (isNetworkServer)
                {
                    int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                    try
                    {
                        EffectSO.SetNetworkRng(new System.Random(seed));
                        Command.RunDeferred(UniqueCreatureID, () =>
                            EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                    }
                    finally
                    {
                        EffectSO.ClearNetworkRng();
                    }
                    ZoneCombatResolver.RecordOnDeathBattleReplay(UniqueCreatureID, i, seed);
                }
                // Client réseau : ne résout rien ici — rejoué via ReplayOnDeathBattleEffect
                // à partir des triplets (sourceID, effectIndex, seed) diffusés par le serveur.
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OnDeath] Exception pendant l'effet OnDeath #{i} ({data.EffectName}) sur {DisplayName} (ID:{UniqueCreatureID}) : {e}");
            }
        }
    }

    // Rejeu côté client (jamais côté serveur, qui a déjà résolu l'effet réellement dans
    // ResolvePredictedBattleDeath) d'un triplet (source, index d'effet, seed) diffusé par le
    // serveur — même mécanisme déterministe que PhaseEffectPipeline.ApplyCanonicalResolution :
    // la seed reproduit exactement le même ciblage aléatoire, donc pas besoin de sérialiser la
    // cible ni le delta appliqué séparément.
    public static void ReplayOnDeathBattleEffect(int sourceCreatureID, int effectIndex, int seed)
    {
        if (!CreaturesCreatedThisGame.TryGetValue(sourceCreatureID, out CreatureLogic creature)) return;
        if (creature.ca.Effects == null || effectIndex < 0 || effectIndex >= creature.ca.Effects.Count) return;

        CardEffectData data = creature.ca.Effects[effectIndex];
        if (data.Trigger != TriggerType.OnDeath) return;

        creature.OnDeathResolvedInBattle = true;
        try
        {
            EffectSO.SetNetworkRng(new System.Random(seed));
            Command.RunDeferred(sourceCreatureID, () =>
                EffectRegistry.Execute(data, new EffectContext { Caster = creature.owner, Source = creature }));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[OnDeath][Replay] Exception pendant le rejeu de l'effet OnDeath #{effectIndex} ({data.EffectName}) sur {creature.DisplayName} (ID:{sourceCreatureID}) : {e}");
        }
        finally
        {
            EffectSO.ClearNetworkRng();
        }
    }

// Résout OnBattleStart pour cette créature au tout début de la planification de sa zone
    // (voir ZoneCombatResolver.ResolveOnBattleStartEffects, appelé avant BuildAutoBattleSequence).
    // zoneDeferKey identifie la zone (IDFactory.GetLocalOnlyID(), voir ZoneCombatResolver.zoneDeferKey) :
    // les commandes visuelles sont reportées (Command.RunDeferred) jusqu'à ce que cette zone
    // commence réellement à jouer ses propres commandes de combat — flush dans
    // ZoneCombatResolver.EnqueueBattleCommands — pour ne pas afficher un buff/dégât avant que la
    // BattleCam n'ait atteint la zone concernée.
    public void ResolveBattleStartEffects(int zoneDeferKey)
    {
        if (IsPendingDeath) return; // tuée par un OnBattleStart précédent dans la même zone
        if (ca.Effects == null) return;

        bool isNetworkServer = NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer;
        for (int i = 0; i < ca.Effects.Count; i++)
        {
            CardEffectData data = ca.Effects[i];
            if (data.Trigger != TriggerType.OnBattleStart) continue;

            try
            {
                if (!NetworkSessionData.IsNetworkSession)
                {
                    Command.RunDeferred(zoneDeferKey, () =>
                        EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                }
                else if (isNetworkServer)
                {
                    int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                    try
                    {
                        EffectSO.SetNetworkRng(new System.Random(seed));
                        Command.RunDeferred(zoneDeferKey, () =>
                            EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                    }
                    finally
                    {
                        EffectSO.ClearNetworkRng();
                    }
                    ZoneCombatResolver.RecordOnBattleStartReplay(zoneDeferKey, UniqueCreatureID, false, i, seed);
                }
                // Client réseau : ne résout rien ici — rejoué via ReplayBattleStartEffect
                // à partir des triplets diffusés par le serveur.
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OnBattleStart] Exception pendant l'effet #{i} ({data.EffectName}) sur {DisplayName} (ID:{UniqueCreatureID}) : {e}");
            }
        }
    }

    // Rejeu côté client d'un effet OnBattleStart déjà résolu par le serveur (même mécanisme
    // déterministe que ReplayOnDeathBattleEffect).
    public void ReplayBattleStartEffect(int zoneDeferKey, int effectIndex, int seed)
    {
        if (ca.Effects == null || effectIndex < 0 || effectIndex >= ca.Effects.Count) return;
        CardEffectData data = ca.Effects[effectIndex];
        if (data.Trigger != TriggerType.OnBattleStart) return;

        try
        {
            EffectSO.SetNetworkRng(new System.Random(seed));
            Command.RunDeferred(zoneDeferKey, () =>
                EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[OnBattleStart][Replay] Exception pendant le rejeu de l'effet #{effectIndex} ({data.EffectName}) sur {DisplayName} (ID:{UniqueCreatureID}) : {e}");
        }
        finally
        {
            EffectSO.ClearNetworkRng();
        }
    }

    public void GoFace()
    {
        AttacksLeftThisTurn--;
        int targetHealthAfter = owner.otherPlayer.TakeDamage(Attack);
        new CreatureAttackCommand(owner.otherPlayer.PlayerID, UniqueCreatureID, 0, Attack, Health, targetHealthAfter).AddToQueue();
    }

    public void AttackCreature (CreatureLogic target)
    {
        AttacksLeftThisTurn--;
        int targetHealthAfter = target.TakeDamage(Attack);
        int attackerHealthAfter = TakeDamage(target.Attack);
        new CreatureAttackCommand(target.UniqueCreatureID, UniqueCreatureID, target.Attack, Attack, attackerHealthAfter, targetHealthAfter).AddToQueue();
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
        int targetHealthAfter = target.TakeDamage(Attack);
        new CreatureAttackCommand(target.ID, UniqueCreatureID, 0, Attack, Health, targetHealthAfter).AddToQueue();
    }

    public void Move(int baseID, int tablePos)
    {
        MovementsLeftThisTurn--;
        BaseID = baseID;
        FogOfWarManager.Refresh();
        new CreatureMoveCommand(UniqueCreatureID, baseID, tablePos).AddToQueue();
    }

    /// <summary>
    /// Repositionne une créature après la résolution d'un combat de croisement (survivant qui
    /// continue vers sa destination, ou qui rentre à son origine). Ne consomme pas de mouvement
    /// du tour (déjà consommé lors du Move() initial) — ce n'est pas un déplacement du joueur.
    /// </summary>
    public void RelocateAfterCombat(int baseID, int tablePos)
    {
        BaseID = baseID;
        FogOfWarManager.Refresh();
        new CreatureMoveCommand(UniqueCreatureID, baseID, tablePos).AddToQueue();
    }

    public static void ProcessPendingDeaths()
    {
        List<CreatureLogic> toProcess = new (PendingDeathList);
        PendingDeathList.Clear();
        string processRole = !NetworkSessionData.IsNetworkSession ? "" : NetworkManager.Singleton.IsServer ? "[Server]" : "[Client]";
        Debug.Log($"[Death]{processRole} ProcessPendingDeaths — {toProcess.Count} unité(s) à traiter: [{string.Join(", ", toProcess.ConvertAll(c => $"{c.DisplayName}(ID:{c.UniqueCreatureID})"))}]");
        foreach (CreatureLogic creature in toProcess)
            creature.Die();
    }

    // STATIC For managing IDs
    public static Dictionary<int, CreatureLogic> CreaturesCreatedThisGame = new Dictionary<int, CreatureLogic>();


}
