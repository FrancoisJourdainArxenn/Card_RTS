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

    // Compteur pour CondCounter (source = WrappedCondition) — vit sur l'instance (pas sur le
    // ConditionSO, stateless/partagé entre parties), clé par condition pour supporter plusieurs
    // compteurs indépendants.
    private Dictionary<ConditionSO, int> _conditionCounters;
    public int IncrementConditionCounter(ConditionSO key)
    {
        _conditionCounters ??= new Dictionary<ConditionSO, int>();
        _conditionCounters.TryGetValue(key, out int current);
        current++;
        _conditionCounters[key] = current;
        return current;
    }
    // Lecture seule, pour l'UI (texte de progression) — contrairement à IncrementConditionCounter,
    // n'avance pas le compteur.
    public int PeekConditionCounter(ConditionSO key) =>
        _conditionCounters != null && _conditionCounters.TryGetValue(key, out int v) ? v : 0;

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
    public bool IsPendingMove { get; set; }
    public static List<CreatureLogic> PendingDeathList = new List<CreatureLogic>();
    private bool _deathVisualQueued;

    // True once OnDeath has already been resolved ahead of time during battle planning
    // (see ResolvePredictedBattleDeath). Prevents NotifyCreatureDied from firing OnDeath
    // a second time when the real Die() eventually runs (end of phase / drain).
    public bool OnDeathResolvedInBattle { get; private set; }

    // True once the reactive OnFriendlyCreatureDies/OnEnemyCreatureDies listeners (on OTHER
    // creatures) have already been fired ahead of time for THIS death during battle planning
    // (see ResolvePredictedBattleDeath / EffectRegistry.NotifyCreatureDiedPredicted). Prevents
    // NotifyCreatureDied from firing the creature-owned listeners a second time when the real
    // Die() eventually runs — building-owned listeners are not covered by the predictive path
    // yet, so NotifyCreatureDied still fires those regardless of this flag.
    public bool ReactiveDeathTriggersResolvedInBattle { get; private set; }

    public int ShieldValue { get; private set; } = 0;
    public GameObject ShieldVfxPrefab { get; private set; }

    public void ApplyShield(int value, GameObject vfxPrefab = null)
    {
        ShieldValue += value;
        if (vfxPrefab != null)
            ShieldVfxPrefab = vfxPrefab;
    }

    public void ConsumeShield(int absorbed)
    {
        if (absorbed <= 0) return;
        ShieldValue -= absorbed;
        VfxManager vfx = Vfx;
        if (vfx != null)
        {
            if (ShieldValue == 0)
                vfx.HideShieldVfx();
            else
                vfx.UpdateShieldVfx(ShieldValue);
        }
    }

    // Comme ConsumeShield, mais pour l'absorption calculée pendant ZoneCombatResolver.EnqueueBattleCommands
    // (combat prédit à l'avance, rejoué plus tard par la file de commandes visuelles). La donnée
    // (ShieldValue) doit changer tout de suite pour que les steps suivants du même combat calculent
    // juste, mais le VFX ne doit PAS être touché en direct ici : à cet instant, la file n'a souvent pas
    // encore rejoué le gain de ce bouclier (voir ApplyShieldCommand), donc mettre à jour le VFX
    // maintenant l'écraserait avant même qu'il apparaisse — symptôme observé : bouclier gagné puis
    // consommé dans le même combat prédit, jamais visible. On met donc en file une commande dédiée qui
    // prendra sa place après le gain dans la séquence visuelle (ConsumeShieldCommand).
    public void ConsumeShieldQueued(int absorbed)
    {
        if (absorbed <= 0) return;
        ShieldValue -= absorbed;
        new ConsumeShieldCommand(UniqueCreatureID, ShieldValue).AddToQueue();
    }

    public void GrantCelerity()
    {
        MovementsLeftThisTurn = Mathf.Max(MovementsLeftThisTurn, movementsForOneTurn);
        HasSummoningSickness = false;
        Debug.Log($"[Celerity] {DisplayName} (ID:{UniqueCreatureID}) — movementsForOneTurn={movementsForOneTurn}, MovementsLeftThisTurn={MovementsLeftThisTurn}, GO exists={IDHolder.GetGameObjectWithID(UniqueCreatureID) != null}");

        TurnManager.RefreshAllPlayableHighlights();

    }


    public int TakeDamage(int dmg)
    {
        int rawIncomingDamage = dmg; // pour OnTakeDamage : même sémantique que le hook auto-battle
                                      // (AddPendingCreatureDamage), qui déclenche sur le montant BRUT
                                      // assigné, avant absorption par le bouclier.
        if (ShieldValue > 0)
        {
            int absorbed = Mathf.Min(dmg, ShieldValue);
            ConsumeShield(absorbed);
            dmg -= absorbed;
            if (absorbed > 0) owner.matchStats.Add(MatchStatType.ShieldDamageAbsorbed, absorbed);
        }
        if (dmg > 0) owner.matchStats.Add(MatchStatType.DamageTaken, dmg);
        Health -= dmg;
        if (rawIncomingDamage > 0)
            ResolveOnTakeDamageFromEffect(rawIncomingDamage);
        return Health;
    }

    // Résout OnTakeDamage pour des dégâts qui NE PASSENT PAS par la simulation prédictive de combat
    // (ZoneCombatResolver.AddPendingCreatureDamage / ResolvePredictedOnTakeDamage) — ex: DealDamageSO
    // (deathrattle "Acid Explosion", sort de dégâts direct, etc.). ZoneCombatResolver ne passe JAMAIS
    // par TakeDamage() (il mute Health directement), donc aucun risque de double-déclenchement avec le
    // hook auto-battle — les deux chemins sont mutuellement exclusifs par construction.
    //
    // Deux cas, distingués par Command.CurrentDeferSourceID :
    //  - Aucun contexte différé actif (ex: sort de dégâts joué normalement en Commandement, hors de
    //    toute résolution prédictive) : résolution immédiate et locale, exactement comme
    //    EffectRegistry.NotifyCreatureDied le fait déjà pour OnDeath hors combat — la synchronisation
    //    réseau de CET appel est déjà assurée par l'appelant englobant (PhaseEffectPipeline.
    //    ApplyCanonicalResolution ou équivalent), comme pour n'importe quel autre effet imbriqué.
    //  - Contexte différé de combat déjà actif (ex: Acid Explosion résolu pendant un OnDeath prédictif) :
    //    se greffe sur CETTE MÊME clé plutôt que d'en ouvrir une nouvelle (même idiome que
    //    TokenGenerationSO.Execute), ET, côté client réseau, NE RÉSOUT RIEN ICI — le serveur a déjà
    //    enregistré ce déclenchement précis comme une entrée séparée de PredictedTriggerReplay, rejouée
    //    par ReplayPredictedTriggerEffect via la boucle plate de ApplyCanonicalBattleAssignmentClientRpc.
    //    Résoudre aussi ici côté client le déclencherait DEUX FOIS : une fois via cette entrée dédiée, une
    //    fois comme effet de bord naturel du REJEU de l'effet englobant (qui ré-exécute sa propre logique
    //    avec la même seed pour reproduire les mêmes cibles, donc rappelle TakeDamage() lui-même).
    private void ResolveOnTakeDamageFromEffect(int dmg)
    {
        if (ca.Effects == null) return;

        int? activeDeferKey = Command.CurrentDeferSourceID;

        if (!activeDeferKey.HasValue)
        {
            for (int i = 0; i < ca.Effects.Count; i++)
            {
                CardEffectData data = ca.Effects[i];
                if (data.Trigger != TriggerType.OnTakeDamage) continue;
                try
                {
                    EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this });
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[OnTakeDamage] Exception pendant l'effet OnTakeDamage #{i} ({data.EffectName}) sur {DisplayName} (ID:{UniqueCreatureID}) : {e}");
                }
            }
            return;
        }

        // Rejoué par l'appelant englobant sur le client — rien à résoudre ici (voir commentaire ci-dessus).
        if (NetworkSessionData.IsNetworkSession && !NetworkManager.Singleton.IsServer)
            return;

        bool isNetworkServer = NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer;
        for (int i = 0; i < ca.Effects.Count; i++)
        {
            CardEffectData data = ca.Effects[i];
            if (data.Trigger != TriggerType.OnTakeDamage) continue;

            try
            {
                if (!isNetworkServer)
                {
                    Command.RunDeferred(activeDeferKey.Value, () =>
                        EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                }
                else
                {
                    int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                    // Sauvegarde/restauration explicite (pas ClearNetworkRng) : on peut être imbriqué
                    // dans le SetNetworkRng d'un trigger englobant encore actif (ex: Acid Explosion
                    // pendant un OnDeath) — le vider clobbererait sa seed avant qu'il ait fini. Idem pour
                    // IsResolvingPredictedTrigger : on ne le referme que si c'est nous qui l'avons ouvert.
                    System.Random previousRng = EffectSO.CurrentNetworkRng;
                    bool alreadyResolving = ZoneCombatResolver.IsResolvingPredictedTrigger;
                    try
                    {
                        EffectSO.SetNetworkRng(new System.Random(seed));
                        if (!alreadyResolving)
                            ZoneCombatResolver.BeginResolvingPredictedTrigger();
                        Command.RunDeferred(activeDeferKey.Value, () =>
                            EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                    }
                    finally
                    {
                        if (!alreadyResolving)
                            ZoneCombatResolver.EndResolvingPredictedTrigger();
                        EffectSO.SetNetworkRng(previousRng);
                    }
                    ZoneCombatResolver.RecordPredictedTriggerReplay(UniqueCreatureID, i, seed, activeDeferKey.Value);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OnTakeDamage] Exception pendant l'effet OnTakeDamage #{i} ({data.EffectName}) sur {DisplayName} (ID:{UniqueCreatureID}) : {e}");
            }
        }
    }

    // returns true if we can attack with this creature now
    public bool CanAttack
    {
        get
        {
            if (ca.IsStructureUnit) return false;
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
            if (ca.IsStructureUnit) return false;
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
        // Verrouillée à 0 pour une structure, quelle que soit la source (buff, ApplyBuff, sync réseau).
        get => ca.IsStructureUnit ? 0 : baseAttack + permAttackBonus + tempAttackBonus;
        set
        {
            if (ca.IsStructureUnit) return;
            permAttackBonus = value - baseAttack - tempAttackBonus;
        }
    }
     
    // number of attacks for one turn if (attacksForOneTurn==2) => Windfury
    private int attacksForOneTurn = 1;
    public int AttacksLeftThisTurn { get; set; }

    // number of movements for one turn if (movementsForOneTurn==2) => Celerity
    private int movementsForOneTurn = 1;
    public int MovementsLeftThisTurn { get; set; }

    // Vrai depuis l'entrée en jeu (main ou token) jusqu'au début du prochain tour de son propriétaire,
    // sauf célérité (voir constructeur / GrantCelerity). Contrairement à (MovementsLeftThisTurn <= 0),
    // ne redevient jamais vrai après un déplacement normal plus tard dans la partie.
    public bool HasSummoningSickness { get; private set; }

    public ZoneLogic Zone => owner.GetPlayerAreaByID(BaseID)?.parentZone.Logic;

    public bool IsMelee => ca.melee;
    public bool IsRanged => !ca.melee;
    public bool IsShielded => ShieldValue > 0;

    // Modificateurs octroyés à l'exécution (ex: GrantAttackModifierSO), distincts de ca.AttackModifiers :
    // ca est un ScriptableObject PARTAGÉ par toutes les instances de cette carte dans la partie, donc y
    // ajouter directement contaminerait toutes les autres copies de la carte, pas seulement cette créature.
    private List<AttackModifierSO> _grantedAttackModifiers;
    public List<AttackModifierSO> AttackModifiers
    {
        get
        {
            if (_grantedAttackModifiers == null || _grantedAttackModifiers.Count == 0)
                return ca.AttackModifiers;
            List<AttackModifierSO> combined = new List<AttackModifierSO>(ca.AttackModifiers ?? new List<AttackModifierSO>());
            combined.AddRange(_grantedAttackModifiers);
            return combined;
        }
    }

    public void GrantAttackModifier(AttackModifierSO modifier)
    {
        if (modifier == null) return;
        _grantedAttackModifiers ??= new List<AttackModifierSO>();
        _grantedAttackModifiers.Add(modifier);
    }

    public void RemoveAttackModifier(AttackModifierSO modifier)
    {
        _grantedAttackModifiers?.Remove(modifier);
    }

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
        HasSummoningSickness = !ca.Celerity;
        this.owner = owner;
        this.BaseID = baseID;
        UniqueCreatureID = networkID >= 0 ? networkID : IDFactory.GetUniqueID();
        CreaturesCreatedThisGame.Add(UniqueCreatureID, this);
        owner.matchStats.Add(MatchStatType.UnitsSummoned);

        foreach (PermanentCreatureBuff buff in owner.permanentCreatureBuffs)
            if (buff.filter != null && buff.filter.Matches(ca))
                ApplyBuff(buff.attackBonus, buff.healthBonus);

        if (ca.Effects != null && ca.Effects.Count > 0)
            EffectRegistry.RegisterCreatureEffects(this, ca);
    }

    // METHODS
    public void OnTurnStart()
    {
        AttacksLeftThisTurn = attacksForOneTurn;
        MovementsLeftThisTurn = movementsForOneTurn;
        HasSummoningSickness = false;
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
        EffectRegistry.NotifyCreatureDeathStats(owner);
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
    // planification).
    //
    // Si on est DANS un Command.RunDeferred (résolution anticipée OnBattleStart/OnDeath), la clé de
    // report et la décision de reporter doivent être capturées MAINTENANT, à l'instant exact de la
    // mort — pas plus tard dans QueuePendingDeathVisuals() : entre les deux, un tout AUTRE contexte
    // (une autre zone, un autre RunDeferred) peut s'exécuter et changer Command.DeferForBattleReplay/
    // CurrentDeferSourceID, ce qui ferait rejouer cette mort EN DIRECT au lieu de la laisser reportée
    // — détruisant la créature avant que sa propre zone n'ait fini de révéler ses effets OnBattleStart
    // (symptôme observé : désync réseau + ShowDeathPending sur un GameObject déjà détruit).
    private void MarkPendingDeath()
    {
        health = 0;
        IsPendingDeath = true;
        PendingDeathList.Add(this);

        if (Command.DeferForBattleReplay)
        {
            int deferKey = Command.CurrentDeferSourceID ?? UniqueCreatureID;
            CreatureDieCommand dieCommand = new CreatureDieCommand(UniqueCreatureID, owner);
            Command.DeferDeath(deferKey, dieCommand.AddToQueueImmediate);
            Command.DeferDeath(deferKey, () => Command.FlushDeferredCommands(UniqueCreatureID));
            _deathVisualQueued = true;
        }
    }

    // Met en file la CreatureDieCommand de toute créature marquée mourante EN DEHORS d'un contexte
    // différé (les morts survenues À L'INTÉRIEUR d'un Command.RunDeferred sont déjà prises en charge
    // à l'instant même de la mort, voir MarkPendingDeath ci-dessus). Appelé par l'orchestrateur juste
    // après avoir mis en file ses propres commandes "cause" (voir Command.FlushPendingDeaths).
    public static void QueuePendingDeathVisuals()
    {
        foreach (CreatureLogic creature in PendingDeathList)
        {
            if (creature._deathVisualQueued) continue;
            creature._deathVisualQueued = true;
            new CreatureDieCommand(creature.UniqueCreatureID, creature.owner).AddToQueue();
            Command.FlushDeferredCommands(creature.UniqueCreatureID);
        }
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
        ReactiveDeathTriggersResolvedInBattle = true;

        bool isNetworkServer = NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer;

        // OnDeathResolvedInBattle sert aussi de signal générique "cette créature est déjà morte dans le
        // combat prédictif en cours" pour les requêtes de ciblage d'AUTRES effets (voir
        // TargetFunctions.GetTargetsByTeam, qui exclut IsPendingDeath || OnDeathResolvedInBattle) — pas
        // seulement pour empêcher son propre OnDeath de se redéclencher. Il faut donc le propager au
        // client MÊME quand cette créature n'a elle-même AUCUN effet OnDeath (ca.Effects vide/sans
        // TriggerType.OnDeath) : sinon le client ne l'apprend que bien plus tard, via son propre passage
        // [Enqueue:X] (ScheduleBattleDeath), largement après que d'autres triggers prédits (ex: un OnDeath
        // à ciblage aléatoire d'une autre créature) aient déjà lu son pool de cibles — elle y apparaît
        // alors encore "vivante" côté client alors qu'elle est déjà exclue côté hôte. Repro concret :
        // Flag-Bearer (aucun OnDeath) meurt en contre-attaque puis Baneling meurt et déclenche Acid
        // Explosion (ciblage aléatoire, exclut les IsPendingDeath/OnDeathResolvedInBattle) — côté hôte
        // Flag-Bearer est déjà exclue, côté client elle apparaît encore comme cible éligible → la
        // répartition aléatoire (même seed, pool de taille différente) touche une créature différente.
        // effectIndex=-1 est un sentinel "rien à exécuter, juste marquer OnDeathResolvedInBattle" — géré
        // dans ReplayPredictedTriggerEffect avant la résolution normale d'un CardEffectData.
        if (isNetworkServer)
            ZoneCombatResolver.RecordPredictedTriggerReplay(UniqueCreatureID, -1, 0, UniqueCreatureID);

        if (ca.Effects != null)
        {
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
                            ZoneCombatResolver.BeginResolvingPredictedTrigger();
                            Command.RunDeferred(UniqueCreatureID, () =>
                                EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                        }
                        finally
                        {
                            ZoneCombatResolver.EndResolvingPredictedTrigger();
                            EffectSO.ClearNetworkRng();
                        }
                        ZoneCombatResolver.RecordPredictedTriggerReplay(UniqueCreatureID, i, seed, UniqueCreatureID);
                    }
                    // Client réseau : ne résout rien ici — rejoué via ReplayPredictedTriggerEffect
                    // à partir des triplets (sourceID, effectIndex, seed) diffusés par le serveur.
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[OnDeath] Exception pendant l'effet OnDeath #{i} ({data.EffectName}) sur {DisplayName} (ID:{UniqueCreatureID}) : {e}");
                }
            }
        }

        // Notifie immédiatement les AUTRES créatures qui réagissent à cette mort (ex: Rex "+1/+0
        // quand un allié meurt", trigger OnFriendlyCreatureDies/OnEnemyCreatureDies) — même logique
        // "immédiate" que le OnDeath ci-dessus, pour que ce genre de buff s'applique au reste de CE
        // combat et soit visible dès la mort de cette créature, au lieu d'attendre la fin de la
        // phase Battle (voir EffectRegistry.NotifyCreatureDiedPredicted). Placé APRÈS le OnDeath de
        // cette créature elle-même, pour préserver le même ordre relatif que NotifyCreatureDied
        // (chemin hors combat) : deathrattle propre d'abord, réactions des autres ensuite.
        // Inconditionnel (pas dans le bloc ca.Effects != null ci-dessus) : une créature SANS aucun
        // effet propre peut quand même faire réagir Rex.
        EffectRegistry.NotifyCreatureDiedPredicted(this, owner);
    }

    // Clé de report dédiée à OnAttack, distincte de UniqueCreatureID (utilisé tel quel comme clé par
    // OnDeath). Nécessaire parce qu'une créature peut être prédictivement tuée par une contre-attaque
    // PENDANT sa propre résolution d'attaque (AddPendingCreatureDamage → ResolvePredictedBattleDeath,
    // appelé sur l'ATTAQUANT lui-même) : son bucket OnDeath se retrouverait alors sous la même clé que
    // celle que CreatureAttackCommand.EnqueueAttack flush entre le wind-up et la résolution — le flush
    // OnAttack viderait alors PRÉMATURÉMENT le bucket OnDeath (token/buff affichés en plein wind-up, au
    // lieu de juste avant la CreatureDieCommand ultérieure), cassant à la fois l'ordre visuel et la
    // seconde tentative de flush plus tard dans CreatureLogic.QueuePendingDeathVisuals (bucket déjà vidé,
    // donc silencieusement no-op). Décalage choisi pour ne collisionner ni avec les IDs réels (toujours
    // positifs, IDFactory.GetUniqueID), ni avec la plage des IDs locaux (~-1 000 000, IDFactory.GetLocalOnlyID),
    // ni avec celle de zoneDeferKey (~-2 000 000 000, ZoneCombatResolver.ZoneDeferKeyBase).
    private const int OnAttackDeferKeyBase = -1_500_000_000;
    public static int OnAttackDeferKey(int creatureID) => OnAttackDeferKeyBase - creatureID;

    // Résout OnAttack immédiatement, à l'instant où ZoneCombatResolver assigne effectivement une
    // cible à cette attaque pendant la planification (AssignSingleAttack) — pas d'attente du
    // déroulé visuel. Contrairement à ResolvePredictedBattleDeath, pas besoin de flag "déjà résolu" :
    // AssignSingleAttack n'appelle chaque attaquant qu'une seule fois par bataille de zone
    // (BuildAttackQueue n'ajoute chaque créature qu'une fois à la file d'attaque).
    public void ResolvePredictedOnAttack(ILivable attackTarget = null)
    {
        if (ca.Effects == null) return;

        bool isNetworkServer = NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer;
        for (int i = 0; i < ca.Effects.Count; i++)
        {
            CardEffectData data = ca.Effects[i];
            if (data.Trigger != TriggerType.OnAttack) continue;

            Debug.Log($"[OnAttack] Résolution — {DisplayName} (ID:{UniqueCreatureID}) effet #{i} ({data.EffectName})");

            // try/catch : même raison que ResolvePredictedBattleDeath ci-dessus — une exception ici
            // ne doit jamais laisser Command.DeferForBattleReplay/EffectSO._networkRng bloqués.
            try
            {
                if (!NetworkSessionData.IsNetworkSession)
                {
                    Command.RunDeferred(OnAttackDeferKey(UniqueCreatureID), () =>
                        EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this, Target = attackTarget }));
                }
                else if (isNetworkServer)
                {
                    int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                    try
                    {
                        EffectSO.SetNetworkRng(new System.Random(seed));
                        ZoneCombatResolver.BeginResolvingPredictedTrigger();
                        Command.RunDeferred(OnAttackDeferKey(UniqueCreatureID), () =>
                            EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this, Target = attackTarget }));
                    }
                    finally
                    {
                        ZoneCombatResolver.EndResolvingPredictedTrigger();
                        EffectSO.ClearNetworkRng();
                    }
                    ZoneCombatResolver.RecordPredictedTriggerReplay(UniqueCreatureID, i, seed, OnAttackDeferKey(UniqueCreatureID), targetID: attackTarget?.ID ?? -1);
                }
                // Client réseau : ne résout rien ici — rejoué via ReplayPredictedTriggerEffect
                // à partir des triplets (sourceID, effectIndex, seed) diffusés par le serveur.
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OnAttack] Exception pendant l'effet OnAttack #{i} ({data.EffectName}) sur {DisplayName} (ID:{UniqueCreatureID}) : {e}");
            }
        }
    }

    // Résout OnTakeDamage immédiatement, à l'instant où ZoneCombatResolver ajoute des dégâts prédits sur
    // cette créature pendant la planification (AddPendingCreatureDamage) — quel que soit le montant, et
    // même si ce coup la tue. Contrairement à ResolvePredictedBattleDeath/ResolvePredictedOnAttack, aucun
    // garde "déjà résolu" : une créature peut être touchée plusieurs fois dans la MÊME bataille, chaque
    // coup passe sa propre clé de report (hitDeferKey), une par instance de dégâts — voir
    // ZoneCombatResolver.OnTakeDamageDeferKey.
    public void ResolvePredictedOnTakeDamage(int hitDeferKey)
    {
        if (ca.Effects == null) return;

        bool isNetworkServer = NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer;
        for (int i = 0; i < ca.Effects.Count; i++)
        {
            CardEffectData data = ca.Effects[i];
            if (data.Trigger != TriggerType.OnTakeDamage) continue;

            Debug.Log($"[OnTakeDamage] Résolution — {DisplayName} (ID:{UniqueCreatureID}) effet #{i} ({data.EffectName})");

            try
            {
                if (!NetworkSessionData.IsNetworkSession)
                {
                    Command.RunDeferred(hitDeferKey, () =>
                        EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                }
                else if (isNetworkServer)
                {
                    int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                    try
                    {
                        EffectSO.SetNetworkRng(new System.Random(seed));
                        ZoneCombatResolver.BeginResolvingPredictedTrigger();
                        Command.RunDeferred(hitDeferKey, () =>
                            EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                    }
                    finally
                    {
                        ZoneCombatResolver.EndResolvingPredictedTrigger();
                        EffectSO.ClearNetworkRng();
                    }
                    ZoneCombatResolver.RecordPredictedTriggerReplay(UniqueCreatureID, i, seed, hitDeferKey);
                }
                // Client réseau : ne résout rien ici — rejoué via ReplayPredictedTriggerEffect
                // à partir du quadruplet (sourceID, effectIndex, seed, deferKey) diffusé par le serveur.
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OnTakeDamage] Exception pendant l'effet OnTakeDamage #{i} ({data.EffectName}) sur {DisplayName} (ID:{UniqueCreatureID}) : {e}");
            }
        }
    }

    // Rejeu côté client (jamais côté serveur, qui a déjà résolu l'effet réellement dans
    // ResolvePredictedBattleDeath/ResolvePredictedOnAttack) d'un triplet (source, index d'effet, seed)
    // diffusé par le serveur — même mécanisme déterministe que PhaseEffectPipeline.ApplyCanonicalResolution :
    // la seed reproduit exactement le même ciblage aléatoire, donc pas besoin de sérialiser la
    // cible ni le delta appliqué séparément.
    //
    // Généralisé pour OnDeath ET OnAttack (auparavant ReplayOnDeathBattleEffect, filtré sur
    // TriggerType.OnDeath) : l'effectIndex désigne déjà un CardEffectData précis, dont le Trigger
    // est connu des deux côtés (même carte) — pas besoin de le vérifier ni de le transporter. Les
    // deux triggers partagent une seule liste de replay, rejouée dans l'ordre chronologique réel
    // (voir le commentaire sur ZoneCombatResolver.PredictedTriggerReplay) : rejouer OnDeath et
    // OnAttack comme deux boucles séparées risquerait de trahir cet ordre pour un effet à ciblage
    // aléatoire dont le pool de cibles dépend d'un état de vie/mort pas encore rejoué.
    public static void ReplayPredictedTriggerEffect(int sourceCreatureID, int effectIndex, int seed, int deferKey, int eventSubjectID = -1, int targetID = -1)
    {
        if (!CreaturesCreatedThisGame.TryGetValue(sourceCreatureID, out CreatureLogic creature)) return;

        // Sujet de l'évènement réactif (ex: l'allié qui vient de mourir pour Rex "OnFriendlyCreatureDies") —
        // -1 si ce trigger n'en a pas (OnDeath/OnAttack/OnTakeDamage, déclenchés sur la créature elle-même).
        // Toujours résolvable ici : CreaturesCreatedThisGame ne retire jamais ses entrées en cours de partie.
        CreatureLogic eventSubjectCreature = eventSubjectID >= 0 && CreaturesCreatedThisGame.TryGetValue(eventSubjectID, out CreatureLogic subj)
            ? subj : null;

        // Cible de CETTE attaque (OnAttack uniquement) — -1 sinon. Résolue via le même chemin
        // générique que SelectedTarget (PhaseEffectPipeline.ResolveEntityByID) : couvre
        // Creature/Building/Base/Zone, renvoie null pour un Player (pas de voisinage pour un joueur).
        ILivable replayTarget = PhaseEffectPipeline.ResolveEntityByID(targetID) as ILivable;

        // effectIndex=-1 : sentinel posé par ResolvePredictedBattleDeath pour une créature morte en
        // planification SANS effet OnDeath propre — rien à exécuter, juste propager le flag au client
        // (voir le commentaire sur ce sentinel dans ResolvePredictedBattleDeath).
        if (effectIndex == -1)
        {
            creature.OnDeathResolvedInBattle = true;
            return;
        }

        if (creature.ca.Effects == null || effectIndex < 0 || effectIndex >= creature.ca.Effects.Count) return;

        CardEffectData data = creature.ca.Effects[effectIndex];

        // OnDeathResolvedInBattle sert aussi de garde-fou pour empêcher NotifyCreatureDied de
        // re-déclencher OnDeath plus tard (voir Die()) — pertinent uniquement pour ce trigger précis
        // (le cas générique, sans effet propre, est couvert par le sentinel ci-dessus).
        if (data.Trigger == TriggerType.OnDeath)
            creature.OnDeathResolvedInBattle = true;

        try
        {
            EffectSO.SetNetworkRng(new System.Random(seed));
            Command.RunDeferred(deferKey, () =>
                EffectRegistry.Execute(data, new EffectContext
                    { Caster = creature.owner, Source = creature, EventSubjectCreature = eventSubjectCreature, Target = replayTarget }));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[{data.Trigger}][Replay] Exception pendant le rejeu de l'effet #{effectIndex} ({data.EffectName}) sur {creature.DisplayName} (ID:{sourceCreatureID}) : {e}");
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
    // déterministe que ReplayPredictedTriggerEffect).
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
        IsPendingMove = false;
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
