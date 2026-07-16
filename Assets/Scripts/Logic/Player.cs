using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;

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


    // REFERENCES TO LOGICAL STUFF THAT BELONGS TO THIS PLAYER
    public Deck deck;
    public Hand hand;
    public PlayedCards playedCards;
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
            if (value <= 0)
                Die(); 
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
            if (value < 0)
                mainRessourceAvailable = 0;
            else if (value > mainRessourceTotal)
                mainRessourceAvailable = mainRessourceTotal;
            else
                mainRessourceAvailable = value;
            
            //PArea.ManaBar.AvailableCrystals = manaLeft;
            new UpdateRessourcesCommand(this, mainRessourceTotal, mainRessourceAvailable).AddToQueue();
            //Debug.Log(ManaLeft);
            TurnManager.RefreshAllPlayableHighlights();
            baseVisual?.RefreshUpgradeDisplay();

        }
    }

    public List<CreatureLogic> Creatures => playedCards.Creatures;
    public List<BuildingLogic> Buildings => playedCards.Buildings;
    public List<ZoneLogic> VisibleZones
    {
        get
        {
            var zones = new HashSet<ZoneLogic>();
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
        controlledBaseAssets.Add(baseAsset);
        homeBaseLogic = new BaseLogic(this, MainPArea?.parentZone?.Logic);

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

    public void NetworkSpawnTokenToZone(CardAsset tokenAsset, int cardID, int creatureID, int tablePos, int baseID, EffectVisualData visualData = null)
    {
        PlayerArea targetArea = GetPlayerAreaByID(baseID);
        if (targetArea == null) { Debug.LogError($"[Token] PlayerArea introuvable baseID={baseID}"); return; }

        CardLogic tokenCard = new CardLogic(tokenAsset, cardID);
        tokenCard.owner = this;

        bool tokenIsMelee = tokenAsset.melee;
        int rowLocalPos   = tablePos; // position dans la rangée, envoyée par le serveur
        int logicalIndex  = GetLogicalInsertIndex(tokenAsset.melee, baseID, rowLocalPos);

        CreatureLogic newCreature = new CreatureLogic(this, tokenAsset, baseID, creatureID);
        playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

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

        new PlayACreatureCommand(tokenCard, this, rowLocalPos, creatureID, targetArea).AddToQueue();
        EffectRegistry.ETB(tokenAsset, new EffectContext { Caster = this, Source = newCreature });
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
    // this method is called from Logic, for example by AI
    public void PlayASpellFromHand(CardLogic playedCard, ILivable target)
    {
        MainRessourceAvailable -= playedCard.MainCost;

        EffectRegistry.ETB(playedCard.ca, new EffectContext
        {
            Caster = this,
            Target = target
        });

        new PlayASpellCardCommand(this, playedCard).AddToQueue();
        hand.CardsInHand.Remove(playedCard);
        // Recompute playable state after the card is removed.
        HighlightPlayableCards();
    }

    // METHODS TO PLAY CREATURES 
    // 1st overload - by ID
    public void PlayACreatureFromHand(int UniqueID, int tablePos, PlayerArea selectedPArea)
    {
        PlayACreatureFromHand(CardLogic.CardsCreatedThisGame[UniqueID], tablePos, selectedPArea);
    }

    // 2nd overload - by logic units
    public void PlayACreatureFromHand(CardLogic playedCard, int rowLocalPos, PlayerArea selectedPArea)
    {
        MainRessourceAvailable -= playedCard.MainCost;
        int baseID       = selectedPArea.baseID;
        int logicalIndex = GetLogicalInsertIndex(playedCard.ca.melee, baseID, rowLocalPos);

        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca, baseID);
        playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

        new PlayACreatureCommand(playedCard, this, rowLocalPos, newCreature.UniqueCreatureID, selectedPArea).AddToQueue();
        EffectRegistry.ETB(playedCard.ca, new EffectContext { Caster = this, Target = null, Source = newCreature });
        hand.CardsInHand.Remove(playedCard);
        HighlightPlayableCards();
    }

    // Index d'insertion dans playedCards.Creatures pour maintenir [melee G→D, ranged G→D]
    public int GetLogicalInsertIndex(bool isMelee, int baseID, int rowLocalPos)
    {
        int matchCount = 0;
        for (int i = 0; i < playedCards.Creatures.Count; i++)
        {
            var c = playedCards.Creatures[i];
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
        var ordered = new List<CreatureLogic>();
        foreach (var go in meleeGOs)
        {
            IDHolder id = go?.GetComponent<IDHolder>();
            if (id != null && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id.UniqueID, out var cl))
                ordered.Add(cl);
        }
        foreach (var go in rangedGOs)
        {
            IDHolder id = go?.GetComponent<IDHolder>();
            if (id != null && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id.UniqueID, out var cl))
                ordered.Add(cl);
        }
        playedCards.Creatures.RemoveAll(c => c.BaseID == baseID);
        playedCards.Creatures.AddRange(ordered);
    }

    public void NetworkPendingPlayCreature(int cardUniqueID, int creatureUniqueID, int tablePos, int baseID)
    {
        if (!CardLogic.CardsCreatedThisGame.TryGetValue(cardUniqueID, out CardLogic playedCard)) return;

        MainRessourceAvailable -= playedCard.MainCost;
        hand.CardsInHand.Remove(playedCard);
        TurnManager.RefreshAllPlayableHighlights();

        GameObject cardGO = IDHolder.GetGameObjectWithID(cardUniqueID);
        if (cardGO != null)
        {
            handVisual.RemoveCard(cardGO);
            GameObject.Destroy(cardGO);
        }

        // Only local player sees their own pending creatures
        if (this != GlobalSettings.Instance.localPlayer) return;

        PlayerArea targetArea = GetPlayerAreaByID(baseID);
        if (targetArea == null) return;

        targetArea.tableVisual.AddCreatureAtIndex(playedCard.ca, creatureUniqueID, tablePos, baseID, completeCommand: false);
        GameObject creatureGO = IDHolder.GetGameObjectWithID(creatureUniqueID);
        if (creatureGO != null && creatureGO.TryGetComponent(out OneCreatureManager ocm))
        {
            ocm.SetPending(true);
            ocm.CanReorderNow = true;
            ocm.UpdateGlow();
        }
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

        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca, baseID, creatureUniqueID);
        int logicalIndex = GetLogicalInsertIndex(playedCard.ca.melee, baseID, tablePos);
        playedCards.Creatures.Insert(logicalIndex, newCreature);

        FogOfWarManager.Refresh();

        new PlayACreatureCommand(playedCard, this, tablePos, creatureUniqueID, selectedPArea).AddToQueue();
        EffectRegistry.ETB(newCreature.ca, new EffectContext { Caster = this, Source = newCreature });

        TurnManager.RefreshAllPlayableHighlights();

    }

    public void NetworkApplyCreatureOrder(int baseID, int[] meleeIDs, int[] rangedIDs)
    {
        PlayerArea area = GetPlayerAreaByID(baseID);
        if (area == null)
            return;
        area.tableVisual.ApplyCreatureOrder(meleeIDs, rangedIDs);
    }

    public void NetworkPlayCreatureFromHand(int cardUniqueID, int creatureUniqueID, int tablePos, int baseID)
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

        // Utilise l'ID fourni par le serveur pour garantir la cohérence entre clients
        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca, baseID, creatureUniqueID);
        int logicalIndex = GetLogicalInsertIndex(playedCard.ca.melee, baseID, tablePos);
        playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

        new PlayACreatureCommand(playedCard, this, tablePos, creatureUniqueID, selectedPArea).AddToQueue();

        EffectRegistry.ETB(newCreature.ca, new EffectContext { Caster = this, Source = newCreature });


        hand.CardsInHand.Remove(playedCard);
        TurnManager.RefreshAllPlayableHighlights();
        // HighlightPlayableCards();
    }

    public int TakeDamage(int dmg)
    {
        Health -= dmg;
        return Health;
    }

    public void Die()
    {
        // game over
        // block both players from taking new moves
        MainPArea.ControlsON = false;
        otherPlayer.MainPArea.ControlsON = false;
        // TurnManager.Instance.StopTheTimer();
        new GameOverCommand(this).AddToQueue();
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
            cardManager.CanBePlayedNow = canPlayCards && affordable && !removeAllHighlights;            
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

    public bool CanPlayCreatureInArea(PlayerArea area)
    {
        if (area == null) return false;
        if (!System.Array.Exists(PAreas, a => a == area)) return false;
        if (area == MainPArea) return true;
        NeutralZoneController c = GetNeutralControllerForArea(area);
        if (c == null) return false;
        return PlayerOwnsBaseInController(c); // tag joueur + même controller
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

    public void CalculatePlayerIncome()
    {
        playerMainIncome = bonusMainIncome;
        foreach (int amt in _incomeFromSources.Values)
            playerMainIncome += amt;
        foreach (BaseAsset baseAsset in controlledBaseAssets)
            playerMainIncome += baseAsset.mainRessourceIncome;
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
