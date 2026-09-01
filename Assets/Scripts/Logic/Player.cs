using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public struct PermanentCreatureBuff
{
    public CardFilterSO filter;
    public int attackBonus;
    public int healthBonus;
}

[System.Flags]
public enum EffectCategory
{
    Damage = 1,
    Heal = 2,
    StatBonus = 4,
}

[System.Serializable]
public struct EffectAmplifier
{
    public EffectCategory AppliesTo;
    public int DamageBonus;
    public int HealBonus;
    public int AttackBonus;
    public int HealthBonus;
    public bool SpellsOnly; // true = ne s'applique qu'aux cartes Type == CardType.Action
}

public class Player : MonoBehaviour, ILivable
{
    // PUBLIC FIELDS
    // int ID that we get from ID factory
    public int PlayerID;
    // a Character Asset that contains data about this Hero
    public FactionAsset factionAsset;
    public BaseAsset baseAsset;
    public List<BaseAsset> controlledBaseAssets = new List<BaseAsset>();

    private readonly List<BaseLogic> _controlledBasesCache = new List<BaseLogic>();
    public List<BaseLogic> controlledBases
    {
        get
        {
            _controlledBasesCache.Clear();
            foreach (BaseLogic b in BaseLogic.BasesCreatedThisGame.Values)
                if (b.owner == this) _controlledBasesCache.Add(b);
            return _controlledBasesCache;
        }
    }
    // a script with references to all the visual game objects for this player
    public PlayerArea[] PAreas;
    public PlayerArea MainPArea = null;
    public MainBaseVisual baseVisual;
    public HandVisual handVisual;
    public Color playerColor;

    public int mainRessourceTotal;
    public int mainRessourceAvailable;
    public int playerMainIncome;
    [HideInInspector] public int bonusMainIncome;
    [HideInInspector] public int bonusMainRessource;
    private Dictionary<int, int> _incomeFromSources = new(); // income lié à une entité vivante

    [HideInInspector] public int bonusHandDrawCount;
    private Dictionary<int, int> _drawCountFromSources = new(); // bonus de pioche lié à une entité vivante (tier, effet...)

    public int HandDrawCount => GlobalSettings.Instance.initdraw + bonusHandDrawCount + _drawCountFromSources.Values.Sum();

    private Dictionary<int, int> _shieldBonusFromSources = new(); // bonus de bouclier lié à une entité vivante (aura, effet...)
    public int ShieldBonus => _shieldBonusFromSources.Values.Sum();

    private Dictionary<int, EffectAmplifier> _effectAmplifiersFromSources = new(); // amplificateurs de Damage/Heal/StatBonus liés à une entité vivante

    // Buffs de stats permanents ("pour le reste de la partie") appliqués par SubType ou par nom de
    // carte. Appliqués à chaque nouvelle CreatureLogic de ce joueur (voir CreatureLogic constructor) —
    // pas sur le CardAsset (partagé entre joueurs) pour ne pas buffer l'adversaire.
    public List<PermanentCreatureBuff> permanentCreatureBuffs = new List<PermanentCreatureBuff>();


    // REFERENCES TO LOGICAL STUFF THAT BELONGS TO THIS PLAYER
    public Deck deck;
    public Hand hand;
    public CardLogic ReservedCard; // carte posée dans un CardHoldSlotVisual : exclue de DiscardHand
    public PlayedCards playedCards;
    public HeroCountUnlock matchStats = new HeroCountUnlock();
    [HideInInspector] public BaseLogic homeBaseLogic;


    // a static array that will store both players, should always have 2 players
    public static Player[] Players;

    // PROPERTIES
    // this property is a part of interface ILivable
    public int ID
    {
        get{ return PlayerID; }
    }
    public string DisplayName => name;

    public ZoneLogic Zone => null;

    // opponent player
    public Player otherPlayer
    {
        get
        {
            if (Players[0] == this)
                return Players[1];
            else
                return Players[0];
        }
    }

    private int health;
    public int Health
    {
        get { return health;}
        set
        {
            if (value > baseAsset.MaxHealth)
                health = baseAsset.MaxHealth;
            else
                health = value;
            // Die() is no longer triggered reactively here — the game-over decision is now made
            // ahead of time by ZoneCombatResolver.ComputeRoundOutcome() and acted upon explicitly
            // by GameOverCommand once the decisive main-base combat(s) finish animating. See
            // GameOverCommand.cs / ZoneCombatResolver.EnqueueOrderedBattleCommands.
        }
    }

    public int MaxHealth
    {
        get { return baseAsset.MaxHealth;}
        set { }
    }

    // TODO clean this
    public int Attack {
        get {
            return 0;
        }
        set {} 
    }

    //private int mainRessourceAvailable;
    public int MainRessourceAvailable
    {
        get
        { return mainRessourceAvailable;}
        set
        {
            int previous = mainRessourceAvailable;
            if (value < 0)
                mainRessourceAvailable = 0;
            else if (value > mainRessourceTotal)
                mainRessourceAvailable = mainRessourceTotal;
            else
                mainRessourceAvailable = value;

            int spent = previous - mainRessourceAvailable;
            if (spent > 0)
            {
                matchStats.Add(MatchStatType.RessourcesSpent, spent);
                EffectRegistry.NotifyRessourceSpent(this);
            }

            //PArea.ManaBar.AvailableCrystals = manaLeft;
            new UpdateRessourcesCommand(this, mainRessourceTotal, mainRessourceAvailable).AddToQueue();
            //Debug.Log(ManaLeft);
            TurnManager.RefreshAllPlayableHighlights();
            baseVisual?.RefreshTierIcon();

        }
    }

    public List<CreatureLogic> Creatures => playedCards.Creatures;
    public List<BuildingLogic> Buildings => playedCards.Buildings;
    public List<ZoneLogic> VisibleZones
    {
        get
        {
            HashSet<ZoneLogic> zones = new HashSet<ZoneLogic>();
            foreach (PlayerArea pa in PAreas)
            {
                if (pa == MainPArea
                    || playedCards.Creatures.Exists(c => c.BaseID == pa.baseID)
                    || playedCards.Buildings.Exists(b => b.OriginSpot.Zone == pa.parentZone))
                    zones.Add(pa.parentZone.Logic);
            }
            foreach (BaseLogic bl in controlledBases)
            {
                ZoneLogic z = bl.Zone;
                if (z != null) zones.Add(z);
            }
            return zones.ToList();
        }
    }
    
    // CODE FOR EVENTS TO LET CREATURES KNOW WHEN TO CAUSE EFFECTS
    public delegate void VoidWithNoArguments();
    //public event VoidWithNoArguments CreaturePlayedEvent;
    //public event VoidWithNoArguments SpellPlayedEvent;
    //public event VoidWithNoArguments StartTurnEvent;
    public event VoidWithNoArguments EndTurnEvent;

    public int playerIndex => System.Array.IndexOf(Players, this);
    // ALL METHODS
    void Awake()
    {
        // find all scripts of type Player and store them in Players array
        // (we should have only 2 players in the scene)
        Players = GameObject.FindObjectsByType<Player>(FindObjectsSortMode.None);
        // Trier par position dans la hiérarchie de scène pour garantir un ordre
        // identique sur le host ET le client (FindObjectsByType sans tri n'est pas stable).
        System.Array.Sort(Players, (a, b) =>
            a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        // obtain unique id from IDFactory
        PlayerID = IDFactory.GetUniqueID();
        matchStats.OwnerLabel = $"Player {PlayerID} ({name})";
        controlledBaseAssets.Add(baseAsset);
        homeBaseLogic = new BaseLogic(this);

    }

    // Applique un BaseAsset différent de celui câblé dans l'Inspector (ex: celui du CardPoolSO du
    // deck choisi en menu, résolu seulement dans TurnManager.OnGameStart, donc après Awake()) et
    // rafraîchit tout ce qui avait déjà été initialisé avec l'ancien (visuel + homeBaseLogic).
    public void ApplyBaseAssetOverride(BaseAsset overrideBaseAsset)
    {
        if (overrideBaseAsset == null || overrideBaseAsset == baseAsset)
            return;

        if (controlledBaseAssets.Count > 0 && controlledBaseAssets[0] == baseAsset)
            controlledBaseAssets[0] = overrideBaseAsset;

        baseAsset = overrideBaseAsset;

        if (baseVisual != null && baseVisual.baseManager != null)
            baseVisual.baseManager.ResetValues(baseAsset);

        homeBaseLogic = new BaseLogic(this);
    }

    void Start()
    {
        baseVisual.gameObject.GetComponent<IDHolder>().UniqueID = PlayerID;
        foreach (PlayerArea area in PAreas)
        {
            area.tableVisual.ownerColor = playerColor;
            area.tableVisual.SetOwnerColor(playerColor);
        }

        InitBaseIDs();
    }

    public void InitBaseIDs()
    {
        for(int i = 0; i < PAreas.Length; i++)
        {
            PAreas[i].baseID = i + PlayerID * PAreas.Length;
        }
    }

    public virtual void OnTurnStart() // ICI nécessite de changer l'apport en ressource
    {
        CalculatePlayerIncome(); // recalcule le malus "under attack" avec l'état du plateau à l'instant du versement

        if (baseAsset == null)
        {
            Debug.LogWarning("OnTurnStart() skipped: baseAsset is null for " + name, this);
            return;
        }

        if (mainRessourceAvailable >= mainRessourceTotal)
            mainRessourceAvailable = mainRessourceTotal;
        else
        {
            mainRessourceAvailable += playerMainIncome;
        }
        homeBaseLogic?.TickUpgradeCostDown();


        // Refresh UI + playable state.
        if (this == GlobalSettings.Instance.localPlayer && GlobalSettings.Instance.UiPlayerVisual != null)
        {
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
        }
        if (baseVisual != null)
            baseVisual.ApplyLookFromAsset();

        if (playedCards != null)
        {
            foreach (CreatureLogic cl in playedCards.Creatures)
                cl.OnTurnStart();
            foreach (BuildingLogic bl in playedCards.Buildings)
                bl.OnTurnStart();
        }
    }

    public void OnTurnEnd()
    {
        if(EndTurnEvent != null)
            EndTurnEvent.Invoke();
    }

    // STUFF THAT OUR PLAYER CAN DO

    // get mana from coin or other spells
    public void GetBonusRessources(int mainRessourceAmount)
    {
        bonusMainRessource += mainRessourceAmount;
        MainRessourceAvailable += mainRessourceAmount;
    }

    // FOR TESTING ONLY
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
            DrawACard();

    }

    // draw a single card from the deck
    public void DrawACard(bool fast = false, int netWorkID = -1, int finalSeed = 0)
    {
        if (deck.playerDeck.cards.Count > 0)
        {
            if (hand.CardsInHand.Count < handVisual.slots.Children.Length)
            {
                CardAsset cardDrawn = NetworkSessionData.IsNetworkSession
                    ? deck.DrawWeightedCard(finalSeed, playerMainIncome, gameObject.name)
                    : deck.DrawWeightedCard(UnityEngine.Random.Range(int.MinValue, int.MaxValue),
                                            playerMainIncome, gameObject.name);

                // Debug.Log($"[DrawACard] Player {PlayerID} | finalSeed={finalSeed} → {cardDrawn.name} | netID={netWorkID}");

                CardLogic newCard = new CardLogic(cardDrawn, netWorkID);
                newCard.owner = this;
                hand.CardsInHand.Insert(0, newCard);
                // Debug.Log(hand.CardsInHand.Count);
                // 2) logic: remove the card from the deck
                // deck.cards.RemoveAt(0);
                // 2) create a command
                new DrawACardCommand(hand.CardsInHand[0], this, fast, fromDeck: true).AddToQueue(); 
            }
        }
        else
        {
            // there are no cards in the deck, take fatigue damage.
        }

    }

    // discard the whole hand (called at the end of the Command phase, right before BeginCombat,
    // so cards added to hand during the Battle phase aren't wiped out by this turn's discard)
    // hero cards stay in hand: they aren't drawn from the deck and shouldn't be discarded
    public void DiscardHand()
    {
        List<CardLogic> toDiscard = hand.CardsInHand.Where(cl => !cl.ca.IsHero && cl != ReservedCard).ToList();
        foreach (CardLogic cl in toDiscard)
        {
            GameObject cardGO = IDHolder.GetGameObjectWithID(cl.UniqueCardID);
            if (cardGO != null)
            {
                handVisual.RemoveCard(cardGO);
                GameObject.Destroy(cardGO);
            }
            hand.CardsInHand.Remove(cl);
        }
    }

    // get card NOT from deck (a token or a coin)
    public void GetACardNotFromDeck(CardAsset cardAsset, int networkID = -1, EffectVisualData visualData = null)
    {
        if (hand.CardsInHand.Count < handVisual.slots.Children.Length)
        {
            CardLogic newCard = new CardLogic(cardAsset, networkID);
            newCard.owner = this;
            hand.CardsInHand.Insert(0, newCard);
            new DrawACardCommand(hand.CardsInHand[0], this, fast: true, fromDeck: false, visualData: visualData).AddToQueue();
        }
    }

    public void NetworkSpawnTokenToZone(CardAsset tokenAsset, int cardID, int creatureID, int tablePos, int baseID, int deferKey, EffectVisualData visualData = null)
    {
        PlayerArea targetArea = GetPlayerAreaByID(baseID);
        if (targetArea == null) { Debug.LogError($"[Token] PlayerArea introuvable baseID={baseID}"); return; }

        CardLogic tokenCard = new CardLogic(tokenAsset, cardID);
        tokenCard.owner = this;

        bool tokenIsMelee = tokenAsset.melee;
        int rowLocalPos   = tablePos; // position dans la rangée, envoyée par le serveur
        int logicalIndex  = GetLogicalInsertIndex(tokenAsset.melee, baseID, rowLocalPos);

        CreatureLogic newCreature = new CreatureLogic(this, tokenAsset, baseID, creatureID);
        // Capturé tout de suite, avant que le replay des dégâts de CE combat ne soit appliqué —
        // voir TokenGenerationSO.SpawnToZone pour le même souci côté hôte.
        int spawnAttack = newCreature.Attack;
        int spawnHealth = newCreature.Health;
        playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

        void QueueVisuals()
        {
            if (visualData?.vfxPrefab != null)
            {
                ZoneManager targetZone = targetArea.parentZone;
                bool isVisible = targetZone == null || FogOfWarManager.Instance == null
                                 || !FogOfWarManager.Instance.IsZoneFogged(targetZone);

                if (isVisible)
                {
                    CenteredSlots rowSlots = tokenIsMelee && targetArea.tableVisual.meleeSlots != null
                        ? targetArea.tableVisual.meleeSlots
                        : targetArea.tableVisual.rangedSlots;
                    int currentCount = tokenIsMelee
                        ? targetArea.tableVisual.MeleeCreaturesOnTable.Count
                        : targetArea.tableVisual.RangedCreaturesOnTable.Count;
                    Vector3 spawnPos = rowSlots.GetSlotPosition(rowLocalPos, currentCount + 1);
                    new SpawnVFXCommand(visualData.vfxPrefab, spawnPos).AddToQueue();
                    new DelayCommand(0.9f).AddToQueue();
                }
            }

            new PlayACreatureCommand(tokenCard, this, rowLocalPos, creatureID, targetArea, spawnAttack, spawnHealth).AddToQueue();
            EffectRegistry.ETB(tokenAsset, new EffectContext { Caster = this, Source = newCreature });
            EffectRegistry.NotifyTokenCreated(this, newCreature);
        }

        // Ce ClientRpc peut arriver AVANT le déroulé animé de la bataille (voir BroadcastBattleStepsClientRpc) —
        // sans report, ces Command joueraient immédiatement à la réception, donc en tête de combat,
        // au lieu d'attendre le même moment que côté hôte : la mort de la créature source pour un
        // token OnDeath (Command.FlushDeferredCommands(CreatureLogic.OnDeathDeferKey(...)), voir
        // CreatureLogic.ScheduleBattleDeath), ou l'arrivée de la BattleCam sur la zone pour un token
        // OnBattleStart (Command.FlushDeferredCommands(zoneDeferKey), voir
        // ZoneCombatResolver.EnqueueBattleCommands). deferKey (== int.MinValue si pas de report,
        // voir Command.CurrentDeferSourceID) porte déjà la bonne clé, quel que soit le trigger
        // d'origine — pas besoin de la deviner ici.
        if (deferKey != int.MinValue)
            Command.RunDeferred(deferKey, QueueVisuals);
        else
            QueueVisuals();
    }


    // 2 METHODS FOR PLAYING SPELLS
    // 1st overload - takes ids as arguments
    // it is cnvenient to call this method from visual part
    public void PlayASpellFromHand(int SpellCardUniqueID, int TargetUniqueID)
    {
        if (TargetUniqueID < 0)
            PlayASpellFromHand(CardLogic.CardsCreatedThisGame[SpellCardUniqueID], null);
        else if (TargetUniqueID == ID)
        {
            PlayASpellFromHand(CardLogic.CardsCreatedThisGame[SpellCardUniqueID], this);
        }
        else if (TargetUniqueID == otherPlayer.ID)
        {
            PlayASpellFromHand(CardLogic.CardsCreatedThisGame[SpellCardUniqueID], this.otherPlayer);
        }
        else
        {
            // target is a creature
            PlayASpellFromHand(CardLogic.CardsCreatedThisGame[SpellCardUniqueID], CreatureLogic.CreaturesCreatedThisGame[TargetUniqueID]);
        }
          
    }

    // 2nd overload - takes CardLogic and ILivable interface -
    // this method is called from Logic, for example by AI.
    // preResolvedSelections : cible(s) choisie(s) via OnPlayTargetingSession pour un sort ciblé
    // (voir DragSpellOnTarget) — transmis à EffectRegistry.ETB exactement comme pour une créature.
    public void PlayASpellFromHand(CardLogic playedCard, ILivable target, List<PendingEffectSelection> preResolvedSelections = null)
    {
        MainRessourceAvailable -= playedCard.MainCost;
        matchStats.Add(MatchStatType.CardsPlayed);

        EffectRegistry.ETB(playedCard.ca, new EffectContext
        {
            Caster = this,
            Target = target
        }, preResolvedSelections);

        new PlayASpellCardCommand(this, playedCard).AddToQueue();
        hand.CardsInHand.Remove(playedCard);
        ClearReservedCardIfPlayed(playedCard);
        // Recompute playable state after the card is removed.
        HighlightPlayableCards();
    }

    // Si la carte jouée était celle réservée dans un CardHoldSlotVisual, on la libère ici. Les
    // scripts de drag le font déjà localement (HoldSlot.ReleaseCard) sur la machine qui a dragué la
    // carte hors du slot, mais en réseau les autres machines n'apprennent qu'une carte a été jouée
    // qu'à travers ces méthodes NetworkPendingPlayX/NetworkPlayCreatureFromHand — sans cet appel ici
    // aussi, leur ReservedCard resterait obsolète et DiscardHand (qui tourne indépendamment sur
    // chaque machine) finirait par exclure du discard une carte que l'autre machine a bien discardée.
    private void ClearReservedCardIfPlayed(CardLogic playedCard)
    {
        if (playedCard != ReservedCard) return;
        GameObject cardGO = IDHolder.GetGameObjectWithID(playedCard.UniqueCardID);
        CardHoldSlotVisual slot = cardGO != null ? CardHoldSlotVisual.ForPlayer(this) : null;
        if (slot != null)
            slot.ReleaseCard(cardGO);
        else
            ReservedCard = null;
    }

    // Envoyé par le serveur à TOUS les clients dès qu'un sort est joué (voir
    // GameNetworkManager.PlaySpellServerRpc) : applique l'état de jeu (ressources, main, effets)
    // immédiatement et de façon déterministe sur toutes les machines — miroir de
    // NetworkPendingPlayCreature. Contrairement à une créature, un sort ne place rien sur une
    // table : rien à cacher/révéler visuellement à ce stade, seul l'habillage "carte jouée" est
    // différé jusqu'à NetworkFlushPlaySpell.
    public void NetworkPendingPlaySpell(int cardUniqueID, int[] effectIndexes, int[] selectedTargetIDs, int seed)
    {
        if (!CardLogic.CardsCreatedThisGame.TryGetValue(cardUniqueID, out CardLogic playedCard)) return;

        MainRessourceAvailable -= playedCard.MainCost;
        matchStats.Add(MatchStatType.CardsPlayed);
        hand.CardsInHand.Remove(playedCard);
        ClearReservedCardIfPlayed(playedCard);
        HighlightPlayableCards();

        GameObject cardGO = IDHolder.GetGameObjectWithID(cardUniqueID);
        if (cardGO != null)
            handVisual.PlayASpellFromHand(cardGO);

        List<PendingEffectSelection> preResolvedSelections =
            EffectRegistry.BuildPreResolvedSelections(playedCard.ca, effectIndexes, selectedTargetIDs);

        // Sans ça, un effet à répartition aléatoire (ex: EffectRepartition.RandomSingleTarget, voir
        // "+1/+1 un allié dans la zone ciblée") retombe sur EffectSO.ApplyEffect's Random.Range —
        // l'état du RNG Unity de chaque machine ayant divergé indépendamment, la cible choisie
        // diffère entre host et client jusqu'à la resynchro canonique de fin de tour.
        System.Random previousRng = EffectSO.CurrentNetworkRng;
        EffectSO.SetNetworkRng(new System.Random(seed));
        try
        {
            EffectRegistry.ETB(playedCard.ca, new EffectContext { Caster = this }, preResolvedSelections);
        }
        finally
        {
            EffectSO.SetNetworkRng(previousRng);
        }
    }

    // Envoyé par le serveur au moment de la révélation simultanée (voir GameNetworkManager.ExecuteAction) :
    // ne fait plus que l'habillage visuel "carte jouée" — la logique a déjà été résolue par
    // NetworkPendingPlaySpell sur toutes les machines. Réutilise directement l'animation existante
    // (vol vers PlayPreviewSpot + destroy), déjà utilisée par le chemin local.
    public void NetworkFlushPlaySpell(int cardUniqueID)
    {
        GameObject cardGO = IDHolder.GetGameObjectWithID(cardUniqueID);
        if (cardGO != null)
            handVisual.PlayASpellFromHand(cardGO);
    }

    // METHODS TO PLAY CREATURES
    // 1st overload - by ID
    public void PlayACreatureFromHand(int UniqueID, int tablePos, PlayerArea selectedPArea)
    {
        PlayACreatureFromHand(CardLogic.CardsCreatedThisGame[UniqueID], tablePos, selectedPArea, null);
    }

    // Utilisé quand OnPlayTargetingSession a déjà fait choisir sa/ses cible(s) au joueur
    // avant la pose (voir DragCreatureOnTable.OnEndDrag).
    public void PlayACreatureFromHand(int UniqueID, int tablePos, PlayerArea selectedPArea, List<PendingEffectSelection> preResolvedSelections)
    {
        PlayACreatureFromHand(CardLogic.CardsCreatedThisGame[UniqueID], tablePos, selectedPArea, preResolvedSelections);
    }

    // 2nd overload - by logic units
    public void PlayACreatureFromHand(CardLogic playedCard, int rowLocalPos, PlayerArea selectedPArea)
        => PlayACreatureFromHand(playedCard, rowLocalPos, selectedPArea, null);

    public void PlayACreatureFromHand(CardLogic playedCard, int rowLocalPos, PlayerArea selectedPArea, List<PendingEffectSelection> preResolvedSelections)
    {
        MainRessourceAvailable -= playedCard.MainCost;
        matchStats.Add(MatchStatType.CardsPlayed);
        matchStats.AddSubTypePlayed(playedCard.ca.subType);
        int baseID       = selectedPArea.baseID;
        int logicalIndex = GetLogicalInsertIndex(playedCard.ca.melee, baseID, rowLocalPos);

        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca, baseID);
        playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

        new PlayACreatureCommand(playedCard, this, rowLocalPos, newCreature.UniqueCreatureID, selectedPArea).AddToQueue();
        EffectRegistry.ETB(playedCard.ca, new EffectContext { Caster = this, Target = null, Source = newCreature }, preResolvedSelections);
        EffectRegistry.NotifyCardPlayed(this, newCreature);
        hand.CardsInHand.Remove(playedCard);
        ClearReservedCardIfPlayed(playedCard);
        HighlightPlayableCards();
    }

    // Index d'insertion dans playedCards.Creatures pour maintenir [melee G→D, ranged G→D]
    public int GetLogicalInsertIndex(bool isMelee, int baseID, int rowLocalPos)
    {
        int matchCount = 0;
        for (int i = 0; i < playedCards.Creatures.Count; i++)
        {
            CreatureLogic c = playedCards.Creatures[i];
            if (c.BaseID == baseID && c.IsMelee == isMelee)
            {
                if (matchCount == rowLocalPos) return i;
                matchCount++;
            }
        }
        // Append — pour ranged, s'assurer d'être après tous les melee de cette area
        if (!isMelee)
        {
            for (int i = playedCards.Creatures.Count - 1; i >= 0; i--)
            {
                if (playedCards.Creatures[i].BaseID == baseID && playedCards.Creatures[i].IsMelee)
                    return i + 1;
            }
        }
        return playedCards.Creatures.Count;
    }

    // Resync l'ordre logique après un repositionnement visuel
    public void ResyncCreatureOrderForArea(int baseID, List<GameObject> meleeGOs, List<GameObject> rangedGOs)
    {
        List<CreatureLogic> ordered = new List<CreatureLogic>();
        foreach (GameObject go in meleeGOs)
        {
            IDHolder id = go?.GetComponent<IDHolder>();
            // !IsPendingDeath && Health > 0 : le GameObject d'une créature tuée hors combat (Die()
            // l'a déjà retirée de playedCards.Creatures) peut encore traîner ici un court instant, le
            // temps que CreatureDieCommand (mis en file, pas synchrone) le détruise réellement. Sans
            // ce filtre, ce fantôme se ferait réinsérer dans playedCards.Creatures juste plus bas
            // (AddRange(ordered)) — annulant le retrait de Die() et le figeant en créature "revivue"
            // mais injouable (voir historique bug Assimilate).
            if (id != null && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id.UniqueID, out CreatureLogic cl)
                && !cl.IsPendingDeath && cl.Health > 0)
                ordered.Add(cl);
        }
        foreach (GameObject go in rangedGOs)
        {
            IDHolder id = go?.GetComponent<IDHolder>();
            if (id != null && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id.UniqueID, out CreatureLogic cl)
                && !cl.IsPendingDeath && cl.Health > 0)
                ordered.Add(cl);
        }
        // Créatures déjà présentes en logique dans cette zone mais pas encore visuellement
        // (ex: reveal différé côté réseau) : ne pas les perdre lors du resync. Exclut également les
        // créatures mortes : une créature morte ne devrait déjà plus être dans playedCards.Creatures
        // (Die() l'en retire), donc sa présence ici ne peut venir que d'une réinsertion fautive — ne
        // pas la réintégrer une seconde fois.
        List<CreatureLogic> pendingWithoutGO = playedCards.Creatures.FindAll(c =>
            c.BaseID == baseID && !ordered.Contains(c) && !c.IsPendingDeath && c.Health > 0);

        if (pendingWithoutGO.Count > 0)
            Debug.LogWarning($"[Resync] baseID={baseID} — {pendingWithoutGO.Count} créature(s) sans GO visuel au moment du resync, réintégrée(s) au lieu d'être perdue(s).");

        playedCards.Creatures.RemoveAll(c => c.BaseID == baseID);
        playedCards.Creatures.AddRange(ordered);
        playedCards.Creatures.AddRange(pendingWithoutGO);
    }

    public void NetworkPendingPlayCreature(
        int cardUniqueID, int creatureUniqueID, int tablePos, int baseID,
        int[] onPlayEffectIndexes, int[] onPlaySelectedTargetIDs, int seed)
    {
        if (!CardLogic.CardsCreatedThisGame.TryGetValue(cardUniqueID, out CardLogic playedCard)) return;

        MainRessourceAvailable -= playedCard.MainCost;
        matchStats.Add(MatchStatType.CardsPlayed);
        matchStats.AddSubTypePlayed(playedCard.ca.subType);
        hand.CardsInHand.Remove(playedCard);
        ClearReservedCardIfPlayed(playedCard);
        HighlightPlayableCards();

        GameObject cardGO = IDHolder.GetGameObjectWithID(cardUniqueID);
        if (cardGO != null)
        {
            handVisual.RemoveCard(cardGO);
            GameObject.Destroy(cardGO);
        }

        PlayerArea targetArea = GetPlayerAreaByID(baseID);
        if (targetArea == null) return;

        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca, baseID, creatureUniqueID);
        int logicalIndex = GetLogicalInsertIndex(playedCard.ca.melee, baseID, tablePos);
        playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

        // Visuel : seul le joueur local voit sa créature en attente (reste caché à l'adversaire jusqu'au flush).
        // Créé AVANT les triggers pour que ModifyStatsCommand/VFX trouvent une cible valide (IDHolder déjà enregistré).
        if (this == GlobalSettings.Instance.localPlayer)
        {
            // tablePos est logique (ghosts exclus, voir TableVisual.ToNetworkTablePos) : on le
            // reconvertit en index de liste réel avant d'insérer dans la rangée visuelle, qui peut
            // contenir d'autres ghosts de déplacement en attente.
            int rawTablePos = targetArea.tableVisual.FromNetworkTablePos(playedCard.ca.melee, tablePos);
            targetArea.tableVisual.AddCreatureAtIndex(playedCard.ca, creatureUniqueID, rawTablePos, baseID, completeCommand: false);
            GameObject creatureGO = IDHolder.GetGameObjectWithID(creatureUniqueID);
            if (creatureGO != null && creatureGO.TryGetComponent(out OneCreatureManager ocm))
            {
                ocm.SetPending(true);
                ocm.CanReorderNow = true;
                ocm.UpdateGlow();
            }
        }

        // Résolution logique immédiate (créature + OnPlay), après le reveal visuel local pour garder une cible valide.
        List<PendingEffectSelection> preResolvedSelections =
            EffectRegistry.BuildPreResolvedSelections(newCreature.ca, onPlayEffectIndexes, onPlaySelectedTargetIDs);

        // Voir Player.NetworkPendingPlaySpell : sans ça, un effet OnPlay à répartition aléatoire
        // retombe sur Random.Range, dont l'état diverge indépendamment entre host et client.
        System.Random previousRng = EffectSO.CurrentNetworkRng;
        EffectSO.SetNetworkRng(new System.Random(seed));
        try
        {
            EffectRegistry.ETB(newCreature.ca, new EffectContext { Caster = this, Source = newCreature }, preResolvedSelections);
        }
        catch (System.Exception e)
        {
            string role = !NetworkSessionData.IsNetworkSession ? "" : Unity.Netcode.NetworkManager.Singleton.IsServer ? "[Server]" : "[Client]";
            Debug.LogError($"[NetworkPendingPlayCreature]{role} Exception dans EffectRegistry.ETB pour {newCreature.DisplayName} (ID={creatureUniqueID}) — OnPlay potentiellement non résolu ici : {e}");
        }
        finally
        {
            EffectSO.SetNetworkRng(previousRng);
        }
        EffectRegistry.NotifyCardPlayed(this, newCreature);
    }

    public void NetworkFlushPlayCreature(int cardUniqueID, int creatureUniqueID, int tablePos, int baseID)
    {
        if (!CardLogic.CardsCreatedThisGame.TryGetValue(cardUniqueID, out CardLogic playedCard))
        {
            Debug.LogError($"[Flush] Carte introuvable : cardUniqueID={cardUniqueID}");
            return;
        }
        PlayerArea selectedPArea = GetPlayerAreaByID(baseID);
        if (selectedPArea == null)
        {
            Debug.LogError($"[Flush] PlayerArea introuvable : baseID={baseID}");
            return;
        }

        // CreatureLogic + OnPlay déjà résolus dans NetworkPendingPlayCreature ; ici on ne fait que révéler le visuel.
        new PlayACreatureCommand(playedCard, this, tablePos, creatureUniqueID, selectedPArea).AddToQueue();

        HighlightPlayableCards();

    }

    public void NetworkApplyCreatureOrder(int baseID, int[] meleeIDs, int[] rangedIDs)
    {
        PlayerArea area = GetPlayerAreaByID(baseID);
        if (area == null)
            return;
        area.tableVisual.ApplyCreatureOrder(meleeIDs, rangedIDs);
    }

    public void NetworkPlayCreatureFromHand(
        int cardUniqueID, int creatureUniqueID, int tablePos, int baseID,
        int[] onPlayEffectIndexes, int[] onPlaySelectedTargetIDs, int seed)
    {
        if (!CardLogic.CardsCreatedThisGame.TryGetValue(cardUniqueID, out CardLogic playedCard))
        {
            Debug.LogError($"[Network] Carte introuvable : cardUniqueID={cardUniqueID}");
            return;
        }
        PlayerArea selectedPArea = GetPlayerAreaByID(baseID);
        if (selectedPArea == null)
        {
            Debug.LogError($"[Network] PlayerArea introuvable : baseID={baseID}");
            return;
        }

        MainRessourceAvailable -= playedCard.MainCost;
        matchStats.Add(MatchStatType.CardsPlayed);
        matchStats.AddSubTypePlayed(playedCard.ca.subType);

        // Utilise l'ID fourni par le serveur pour garantir la cohérence entre clients
        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca, baseID, creatureUniqueID);
        int logicalIndex = GetLogicalInsertIndex(playedCard.ca.melee, baseID, tablePos);
        playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

        new PlayACreatureCommand(playedCard, this, tablePos, creatureUniqueID, selectedPArea).AddToQueue();

        List<PendingEffectSelection> preResolvedSelections =
            EffectRegistry.BuildPreResolvedSelections(newCreature.ca, onPlayEffectIndexes, onPlaySelectedTargetIDs);

        // Voir Player.NetworkPendingPlaySpell : sans ça, un effet OnPlay à répartition aléatoire
        // retombe sur Random.Range, dont l'état diverge indépendamment entre host et client.
        System.Random previousRng = EffectSO.CurrentNetworkRng;
        EffectSO.SetNetworkRng(new System.Random(seed));
        try
        {
            EffectRegistry.ETB(newCreature.ca, new EffectContext { Caster = this, Source = newCreature }, preResolvedSelections);
        }
        catch (System.Exception e)
        {
            string role = !NetworkSessionData.IsNetworkSession ? "" : Unity.Netcode.NetworkManager.Singleton.IsServer ? "[Server]" : "[Client]";
            Debug.LogError($"[NetworkPlayCreatureFromHand]{role} Exception dans EffectRegistry.ETB pour {newCreature.DisplayName} (ID={creatureUniqueID}) — OnPlay potentiellement non résolu ici : {e}");
        }
        finally
        {
            EffectSO.SetNetworkRng(previousRng);
        }
        EffectRegistry.NotifyCardPlayed(this, newCreature);


        hand.CardsInHand.Remove(playedCard);
        ClearReservedCardIfPlayed(playedCard);
        HighlightPlayableCards();
    }

    public int TakeDamage(int dmg)
    {
        Health -= dmg;
        return Health;
    }

    // Blocks both players from taking new moves. Invoked explicitly by GameOverCommand once the
    // game-over outcome has been decided — no longer a reactive side effect of Health reaching 0.
    public void Die()
    {
        MainPArea.ControlsON = false;
        otherPlayer.MainPArea.ControlsON = false;
    }

    // METHOD TO SHOW GLOW HIGHLIGHTS
    public void HighlightPlayableCards(bool removeAllHighlights = false)
    {
        bool commandPhase = TurnManager.Instance != null && TurnManager.Instance.IsCommandPhase;
        bool canPlayCards = commandPhase && TurnManager.Instance.MayPlayerUseControlsInPhase(this);

        foreach (CardLogic cl in hand.CardsInHand)
        {
            GameObject g = IDHolder.GetGameObjectWithID(cl.UniqueCardID);
            OneCardManager cardManager = CheckCardManager(g);
            if (cardManager == null)
            {
                // Debug.LogError($"[HighlightPlayableCards] OneCardManager not found for card {cl.UniqueCardID}");
                continue;
            }
            bool affordable = cl.MainCost <= mainRessourceAvailable;
            cardManager.NotifyLockState(cl.IsLocked);
            // CanBeDraggedNow ignore volontairement le coût : une carte trop chère doit rester
            // manipulable (ex: la déposer dans un CardHoldSlotVisual), seule sa pose effective est
            // bloquée par un contrôle de coût séparé (voir DragCreatureOnTable/DragSpellOnTarget/
            // DragSpellNoTarget). CanBePlayedNow, elle, garde le coût : elle pilote le glow "jouable".
            cardManager.CanBeDraggedNow = canPlayCards && !cl.IsLocked && !removeAllHighlights;
            cardManager.CanBePlayedNow = cardManager.CanBeDraggedNow && affordable;
        }

        bool canMove = commandPhase && TurnManager.Instance.MayPlayerUseControlsInPhase(this);

        foreach (CreatureLogic crl in playedCards.Creatures)
        {
            GameObject g = IDHolder.GetGameObjectWithID(crl.UniqueCreatureID);
            if (g == null) continue; // Détruit (mort en auto-battle), cas attendu
            
            OneCreatureManager creatureManager = CheckCreatureManager(g);
            if (creatureManager == null)
            {
                // Debug.LogError($"[HighlightPlayableCards] OneCreatureManager not found for creature {crl.UniqueCreatureID}");
                continue;
            }
            creatureManager.CanReorderNow = canMove && !removeAllHighlights;
            creatureManager.CanMoveNow = canMove && (crl.MovementsLeftThisTurn > 0) && !removeAllHighlights;
            creatureManager.UpdateGlow();
            // Ne touche pas au visuel "pending" si un déplacement (ghost, voir
            // DragCreatureActions.SpawnPendingMoveGhost) OU un embarquement (voir
            // DragCreatureActions.Board — jamais de ghost pour Board) est en attente sur cette
            // créature : ce refresh écraserait sinon son icône/assombrissement.
            if (creatureManager.PendingMoveGhost == null && !creatureManager.HasPendingBoard)
                creatureManager.SetPending(crl.HasSummoningSickness);
        }

        foreach (BuildingLogic bl in playedCards.Buildings)
        {
            GameObject g = IDHolder.GetGameObjectWithID(bl.UniqueBuildingID);
            if (g == null) continue;
            OneBuildingManager bm = g.GetComponent<OneBuildingManager>();
            if (bm == null) continue;
        }

    }

    public OneCreatureManager CheckCreatureManager(GameObject g)
    {
        if (g == null)
        {
            return null;
        }
        OneCreatureManager creatureManager = g.GetComponent<OneCreatureManager>();
        if (creatureManager == null)
        {
            return null;
        }
        return creatureManager;
    }

    public OneCardManager CheckCardManager(GameObject g)
    {
        if (g == null)
        {
            return null;
        }
        OneCardManager cardManager = g.GetComponent<OneCardManager>();
        if (cardManager == null)
        {
            return null;
        }
        return cardManager;
    }

    // START GAME METHODS
    public void LoadCharacterInfoFromAsset()
    {
        if (baseAsset == null)
        {
            Debug.LogWarning("Player.LoadCharacterInfoFromAsset() called but baseAsset is null: " + name, this);
            return;
        }

        Health = baseAsset.MaxHealth;

        baseVisual.player = this;
        if (this == GlobalSettings.Instance.localPlayer && GlobalSettings.Instance.UiPlayerVisual != null)
        {
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
        }
        baseVisual.ApplyLookFromAsset();

    }

    public void TransmitInfoAboutPlayerToVisual()
    {
        if (NetworkSessionData.IsNetworkSession) 
            return;

        //PArea.Portrait.gameObject.AddComponent<IDHolder>().UniqueID = PlayerID;
        if (GetComponent<TurnMaker>() is AITurnMaker)
        {
            // turn off turn making for this character
            MainPArea.AllowedToControlThisPlayer = false;
        }
        else
        {
            // allow turn making for this character
            MainPArea.AllowedToControlThisPlayer = true;
        }
    }

    public PlayerArea SelectedPArea()
    {
        PlayerArea selectedPArea = null;
        foreach (PlayerArea area in PAreas)
        {
            if (area.tableVisual.CursorOverThisTable)
            {
                selectedPArea = area;
                break;
            }
        }
        return selectedPArea;
    }

    public PlayerArea GetPlayerAreaByID(int baseID)
    {
        foreach (PlayerArea area in PAreas)
        {
            if (area.baseID == baseID)
                return area;
        }
        return null;
    }

    private NeutralZoneController GetNeutralControllerForArea(PlayerArea area)
    {
        if (area == null || area.tableVisual == null)
            return null;
        NeutralZoneController[] allControllers = GameObject.FindObjectsByType<NeutralZoneController>(FindObjectsSortMode.None);
        foreach (NeutralZoneController c in allControllers)
        {
            if (c == null || c.tables == null) continue;
            foreach (TableVisual t in c.tables)
            {
                if (t == area.tableVisual)
                    return c;
            }
        }
        return null;
    }

    private bool PlayerOwnsBaseInController(NeutralZoneController controller)
    {
        if (controller == null) return false;
        OneBaseManager[] allBases = GameObject.FindObjectsByType<OneBaseManager>(FindObjectsSortMode.None);
        foreach (OneBaseManager b in allBases)
        {
            if (b == null || b.Spawner == null) continue;
            if (b.tag != this.tag) continue; // base du joueur courant uniquement
            NeutralBaseVisual nv = b.Spawner.GetComponent<NeutralBaseVisual>();
            if (nv != null && nv.neutralBaseController == controller)
                return true;
        }
        return false;
    }

    public bool CanPlayCreatureInArea(PlayerArea area, CardAsset cardToPlay = null)
    {
        if (area == null) return false;
        if (!System.Array.Exists(PAreas, a => a == area)) return false;
        if (area == MainPArea) return true;
        if (HasCommandCreatureInArea(area)) return true;
        if (cardToPlay != null && cardToPlay.Renfort && HasFriendlyCreatureInArea(area)) return true;
        NeutralZoneController c = GetNeutralControllerForArea(area);
        if (c == null) return false;
        return PlayerOwnsBaseInController(c); // tag joueur + même controller
    }

    // "Commandement" : une créature vivante avec ca.Commandement autorise son propriétaire
    // à poser depuis la main dans SA zone (BaseID), même sans base contrôlée là-bas.
    private bool HasCommandCreatureInArea(PlayerArea area)
    {
        return Creatures.Exists(c => c.ca.Commandement && c.BaseID == area.baseID);
    }

    // "Renfort" : la carte en main peut être posée dans toute zone où le joueur a déjà
    // une unité, même sans Commandement et sans base contrôlée là-bas.
    private bool HasFriendlyCreatureInArea(PlayerArea area)
    {
        return Creatures.Exists(c => c.BaseID == area.baseID);
    }
    
    public void AddBonusIncome(int amount)
    {
        bonusMainIncome += amount;
        CalculatePlayerIncome();
    }

    public void AddBonusIncomeFromSource(int sourceID, int amount)
    {
        _incomeFromSources[sourceID] = _incomeFromSources.GetValueOrDefault(sourceID, 0) + amount;
        CalculatePlayerIncome();
    }

    public void RemoveBonusIncomeFromSource(int sourceID)
    {
        if (!_incomeFromSources.Remove(sourceID)) return;
        CalculatePlayerIncome();
    }

    public void AddBonusHandDrawCount(int amount)
    {
        bonusHandDrawCount += amount;
    }

    public void AddBonusHandDrawCountFromSource(int sourceID, int amount)
    {
        _drawCountFromSources[sourceID] = _drawCountFromSources.GetValueOrDefault(sourceID, 0) + amount;
    }

    public void RemoveBonusHandDrawCountFromSource(int sourceID)
    {
        _drawCountFromSources.Remove(sourceID);
    }

    public void AddBonusShieldFromSource(int sourceID, int amount)
    {
        _shieldBonusFromSources[sourceID] = _shieldBonusFromSources.GetValueOrDefault(sourceID, 0) + amount;
    }

    public void RemoveBonusShieldFromSource(int sourceID)
    {
        _shieldBonusFromSources.Remove(sourceID);
    }

    public void AddEffectAmplifier(int sourceID, EffectAmplifier amplifier)
    {
        _effectAmplifiersFromSources[sourceID] = amplifier;
    }

    public void RemoveEffectAmplifier(int sourceID)
    {
        _effectAmplifiersFromSources.Remove(sourceID);
    }

    private bool AmplifierApplies(EffectAmplifier amp, EffectCategory category, CardAsset playedCard) =>
        (amp.AppliesTo & category) != 0
        && (!amp.SpellsOnly || (playedCard != null && playedCard.Type == CardType.Action));

    public int GetDamageBonus(CardAsset playedCard) =>
        _effectAmplifiersFromSources.Values.Where(a => AmplifierApplies(a, EffectCategory.Damage, playedCard)).Sum(a => a.DamageBonus);

    public int GetHealBonus(CardAsset playedCard) =>
        _effectAmplifiersFromSources.Values.Where(a => AmplifierApplies(a, EffectCategory.Heal, playedCard)).Sum(a => a.HealBonus);

    public (int attack, int health) GetStatBonus(CardAsset playedCard)
    {
        IEnumerable<EffectAmplifier> applicable = _effectAmplifiersFromSources.Values.Where(a => AmplifierApplies(a, EffectCategory.StatBonus, playedCard));
        return (applicable.Sum(a => a.AttackBonus), applicable.Sum(a => a.HealthBonus));
    }

    public void CalculatePlayerIncome()
    {
        playerMainIncome = bonusMainIncome;
        foreach (int amt in _incomeFromSources.Values)
            playerMainIncome += amt;

        foreach (BaseLogic b in controlledBases)
        {
            playerMainIncome += b.EffectiveIncome;
            if (!b.IsHomeBase)
            {
                OneBaseManager mgr = IDHolder.GetGameObjectWithID(b.ID)?.GetComponent<OneBaseManager>();
                mgr?.RefreshIncomeDisplay(b.EffectiveIncome, b.IsUnderAttack);
            }
        }

        if (this == GlobalSettings.Instance.localPlayer && GlobalSettings.Instance.UiPlayerVisual != null)
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
        baseVisual.ApplyLookFromAsset();
    }


    // METHODS TO CREATE A NEW BASE 
    // 1st overload - by ID
    public bool CheckIfCanBuild( BaseAsset baseAsset, NeutralZoneController neutralBaseController)
    {
        if (TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
        {
            new ShowMessageCommand("You can't do that right now", 2f).AddToQueue();
            return false;
        }

        foreach (TableVisual table in neutralBaseController.tables)
        {
            if (table.tag == this.tag)
            {
                if (table.MeleeCreaturesOnTable.Count <= 0 && table.RangedCreaturesOnTable.Count <= 0)
                {
                    new ShowMessageCommand("You need to have at least one creature on the selected table to build a base", 2f).AddToQueue();
                    return false;
                }
            }
        }
        if (MainRessourceAvailable < baseAsset.mainRessourceBaseCost)
        {
            new ShowMessageCommand("Insufficient Ressources", 2f).AddToQueue();
            return false;
        }
        // Block build if any enemy table in the zone has creatures
        foreach (TableVisual table in neutralBaseController.tables)
        {
            if (table.tag != this.tag && (table.MeleeCreaturesOnTable.Count > 0 || table.RangedCreaturesOnTable.Count > 0))
            {
                new ShowMessageCommand("You can't build here while enemy units are present", 2f).AddToQueue();
                return false;
            }
        }

        return true;    
    }

    public void RequestBuildNeutralBase(int neutralBaseId)
    {
        if (NetworkSessionData.IsNetworkSession)
            GameNetworkManager.Instance.BuildNeutralBaseServerRpc(playerIndex, neutralBaseId);
        else
            ExecuteBuildNeutralBase(NeutralBaseVisual.Registry[neutralBaseId], IDFactory.GetUniqueID());
    }

    public void RequestUpgradeBase()
    {
        if (homeBaseLogic == null || homeBaseLogic.IsMaxTier) return;

        if (MainRessourceAvailable < homeBaseLogic.CurrentUpgradeCost)
        {
            new ShowMessageCommand("You don't have enough Ressources to upgrade your Base.", 2f).AddToQueue();
            return;
        }

        if (NetworkSessionData.IsNetworkSession)
            GameNetworkManager.Instance.UpgradeBaseServerRpc(playerIndex);
        else
            homeBaseLogic.TryUpgrade();
    }

    public void ExecuteBuildNeutralBase(NeutralBaseVisual neutralBaseVisual, int baseUniqueID)
    {
        new BaseLogic(this, neutralBaseVisual.baseAsset, neutralBaseVisual.neutralBaseController, baseUniqueID);
        new BuildNeutralBaseCommand(baseUniqueID, this, neutralBaseVisual).AddToQueue();
        FogOfWarManager.Refresh();
    }

    public void ShowBuildings(BuildSpotVisual spot)
    {
        // Debug.Log("Show Buildings for player " + PlayerID);
        GlobalSettings.Instance.buildingShop.Show(deck.playerDeck.buildings, spot);
    }

    public void RequestPlaceBuilding(CardAsset building, BuildSpotVisual spot)
    {
        if (NetworkSessionData.IsNetworkSession)
        {
            MainRessourceAvailable -= building.MainCost;
            spot.SpawnPendingBuilding(building, this);
            GameNetworkManager.Instance.PlaceBuildingServerRpc(playerIndex, deck.playerDeck.buildings.IndexOf(building), spot.SpotID);
        }
        else
            ExecutePlaceBuilding(building, spot, IDFactory.GetUniqueID());
    }

    public void ExecutePlaceBuilding(CardAsset building, BuildSpotVisual spot, int buildingUniqueID, bool alreadyPaid = false)
    {
        new PlaceBuildingCommand(building, this, spot, buildingUniqueID, alreadyPaid).AddToQueue();
    }


}
