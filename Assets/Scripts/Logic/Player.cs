using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class Player : MonoBehaviour, ILivable
{
    // PUBLIC FIELDS
    // int ID that we get from ID factory
    public int PlayerID;
    // a Character Asset that contains data about this Hero
    public FactionAsset factionAsset;
    public BaseAsset baseAsset;
    public List<BaseAsset> controlledBases = new List<BaseAsset>();

    // a script with references to all the visual game objects for this player
    public PlayerArea[] PAreas;
    public PlayerArea MainPArea = null;
    public BaseVisual baseVisual;
    public Color playerColor;

    public int mainRessourceTotal;
    public int mainRessourceAvailable;
    public int secondRessourceTotal;
    public int secondRessourceAvailable;
    public int playerMainIncome;
    public int playerSecondIncome;

    // REFERENCES TO LOGICAL STUFF THAT BELONGS TO THIS PLAYER
    public Deck deck;
    public Hand hand;
    public Table table;

    // a static array that will store both players, should always have 2 players
    public static Player[] Players;

    // this value used exclusively for our coin spell
    private int bonusMainRessource = 0;
    private int bonusSecondRessource = 0;


    // PROPERTIES 
    // this property is a part of interface ILivable
    public int ID
    {
        get{ return PlayerID; }
    }

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
            new UpdateRessourcesCommand(this, mainRessourceTotal, mainRessourceAvailable, secondRessourceTotal, secondRessourceAvailable).AddToQueue();
            //Debug.Log(ManaLeft);
            TurnManager.RefreshAllPlayableHighlights();
        }
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
    //private int secondRessourceTotal;
    public int SecondRessourceTotal
    {
        get{ return secondRessourceTotal;}
        set{ secondRessourceTotal = value;}
    }

    public int SecondRessourceAvailable
    {
        get { return secondRessourceAvailable; }
        set
        {
            if (value < 0)
                secondRessourceAvailable = 0;
            else if (value > secondRessourceTotal)
                secondRessourceAvailable = secondRessourceTotal;
            else
                secondRessourceAvailable = value;

            new UpdateRessourcesCommand(this, mainRessourceTotal, mainRessourceAvailable, secondRessourceTotal, secondRessourceAvailable).AddToQueue();

            TurnManager.RefreshAllPlayableHighlights();
        }
    }
    
    // CODE FOR EVENTS TO LET CREATURES KNOW WHEN TO CAUSE EFFECTS
    public delegate void VoidWithNoArguments();
    //public event VoidWithNoArguments CreaturePlayedEvent;
    //public event VoidWithNoArguments SpellPlayedEvent;
    //public event VoidWithNoArguments StartTurnEvent;
    public event VoidWithNoArguments EndTurnEvent;

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
        controlledBases.Add(baseAsset);
        CalculatePlayerIncome();
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
        if (secondRessourceAvailable >= secondRessourceTotal)
            secondRessourceAvailable = secondRessourceTotal;
        else
        {
            secondRessourceAvailable += playerSecondIncome;
        }

        
        // Refresh UI + playable state.
        HighlightPlayableCards();
        if (baseVisual != null)
            baseVisual.ApplyLookFromAsset();

        if (table != null)
        {
            foreach (CreatureLogic cl in table.CreaturesInPlay)
                cl.OnTurnStart();
        }
    }

    public void OnTurnEnd()
    {
        if(EndTurnEvent != null)
            EndTurnEvent.Invoke();
        GetComponent<TurnMaker>().StopAllCoroutines();
    }

    // STUFF THAT OUR PLAYER CAN DO

    // get mana from coin or other spells 
    public void GetBonusRessources(int mainRessourceAmount, int secondRessourceAmount)
    {
        bonusMainRessource += mainRessourceAmount;
        MainRessourceAvailable += mainRessourceAmount;
        bonusSecondRessource += secondRessourceAmount;
        SecondRessourceAvailable += secondRessourceAmount;
    }

    // FOR TESTING ONLY
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
            DrawACard();

    }

    // draw a single card from the deck
    public void DrawACard(bool fast = false)
    {
        if (deck.cards.Count > 0)
        {
            if (hand.CardsInHand.Count < MainPArea.handVisual.slots.Children.Length)
            {
                // 1) logic: add card to hand
                CardLogic newCard = new CardLogic(deck.cards[0]);
                newCard.owner = this;
                hand.CardsInHand.Insert(0, newCard);
                // Debug.Log(hand.CardsInHand.Count);
                // 2) logic: remove the card from the deck
                deck.cards.RemoveAt(0);
                // 2) create a command
                new DrawACardCommand(hand.CardsInHand[0], this, fast, fromDeck: true).AddToQueue(); 
            }
        }
        else
        {
            // there are no cards in the deck, take fatigue damage.
        }

        if (TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Regroup)
        {
            TurnManager.Instance.RegisterEndPhase(this);
        }
       
    }

    // get card NOT from deck (a token or a coin)
    public void GetACardNotFromDeck(CardAsset cardAsset)
    {
        if (hand.CardsInHand.Count < MainPArea.handVisual.slots.Children.Length)
        {
            // 1) logic: add card to hand
            CardLogic newCard = new CardLogic(cardAsset);
            newCard.owner = this;
            hand.CardsInHand.Insert(0, newCard);
            // 2) send message to the visual Deck
            new DrawACardCommand(hand.CardsInHand[0], this, fast: true, fromDeck: false).AddToQueue(); 
        }
        // no removal from deck because the card was not in the deck
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
        SecondRessourceAvailable -= playedCard.SecondCost;
        // cause effect instantly:
        if (playedCard.effect != null)
            playedCard.effect.ActivateEffect(playedCard.ca.specialSpellAmount, target, this);
        else
        {
            Debug.LogWarning("No effect found on card " + playedCard.ca.name);
        }
        // no matter what happens, move this card to PlayACardSpot
        new PlayASpellCardCommand(this, playedCard).AddToQueue();
        // remove this card from hand
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
    public void PlayACreatureFromHand(CardLogic playedCard, int tablePos, PlayerArea selectedPArea)
    {
        // Debug.Log(ManaLeft);
        // Debug.Log(playedCard.CurrentManaCost);
        MainRessourceAvailable -= playedCard.MainCost;
        SecondRessourceAvailable -= playedCard.SecondCost;
        // Debug.Log("Mana Left after played a creature: " + ManaLeft);
        // create a new creature object and add it to Table
        int baseID = selectedPArea.baseID;
        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca, baseID);
        table.CreaturesInPlay.Insert(tablePos, newCreature);
        FogOfWarManager.Refresh();
        // 
        new PlayACreatureCommand(playedCard, this, tablePos, newCreature.UniqueCreatureID, selectedPArea).AddToQueue();
        // cause battlecry Effect
        if (newCreature.effect != null)
            newCreature.effect.WhenACreatureIsPlayed();
        // remove this card from hand
        hand.CardsInHand.Remove(playedCard);
        HighlightPlayableCards();
    }

    /// <summary>
    /// Version réseau : exécute le jeu d'une créature avec un ID fourni par le serveur.
    /// Appelée sur TOUS les clients via PlayCreatureClientRpc.
    /// </summary>
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
        SecondRessourceAvailable -= playedCard.SecondCost;

        // Utilise l'ID fourni par le serveur pour garantir la cohérence entre clients
        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca, baseID, creatureUniqueID);
        table.CreaturesInPlay.Insert(tablePos, newCreature);
        FogOfWarManager.Refresh();

        new PlayACreatureCommand(playedCard, this, tablePos, creatureUniqueID, selectedPArea).AddToQueue();

        if (newCreature.effect != null)
            newCreature.effect.WhenACreatureIsPlayed();

        hand.CardsInHand.Remove(playedCard);
        HighlightPlayableCards();
    }

    public void Die()
    {
        // game over
        // block both players from taking new moves
        MainPArea.ControlsON = false;
        otherPlayer.MainPArea.ControlsON = false;
        TurnManager.Instance.StopTheTimer();
        new GameOverCommand(this).AddToQueue();
    }

    // METHOD TO SHOW GLOW HIGHLIGHTS
    public void HighlightPlayableCards(bool removeAllHighlights = false)
    {
        bool commandPhase = TurnManager.Instance != null && TurnManager.Instance.IsCommandPhase;
        bool battlePhase = TurnManager.Instance != null && TurnManager.Instance.IsBattlePhase;
        bool canPlayCards = commandPhase && TurnManager.Instance.MayPlayerUseControlsInPhase(this);

        foreach (CardLogic cl in hand.CardsInHand)
        {
            GameObject g = IDHolder.GetGameObjectWithID(cl.UniqueCardID);
            if (g != null)
            {
                bool affordable = (cl.MainCost <= mainRessourceAvailable) && (cl.SecondCost <= secondRessourceAvailable);
                g.GetComponent<OneCardManager>().CanBePlayedNow = canPlayCards && affordable && !removeAllHighlights;
            }
        }

        bool canAttack = battlePhase && TurnManager.Instance.MayPlayerUseControlsInPhase(this);
        bool canMove = commandPhase && TurnManager.Instance.MayPlayerUseControlsInPhase(this);

        foreach (CreatureLogic crl in table.CreaturesInPlay)
        {
            GameObject g = IDHolder.GetGameObjectWithID(crl.UniqueCreatureID);
            if (g != null)
            {
                OneCreatureManager creatureManager = g.GetComponent<OneCreatureManager>();
                creatureManager.CanAttackNow = canAttack && (crl.AttacksLeftThisTurn > 0) && !removeAllHighlights;
                creatureManager.CanMoveNow = canMove && (crl.MovementsLeftThisTurn > 0) && !removeAllHighlights;
                // Debug.Log($"[Player] Creature {crl.UniqueCreatureID} canAttackNow={creatureManager.CanAttackNow} canMoveNow={creatureManager.CanMoveNow} movesLeft={crl.MovementsLeftThisTurn} phaseCommand={commandPhase} mayControl={TurnManager.Instance.MayPlayerUseControlsInPhase(this)}");
                creatureManager.UpdateCreatureGlow();
            }

        }

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
        baseVisual.ApplyLookFromAsset();

    }

    public void TransmitInfoAboutPlayerToVisual()
    {
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

    private NeutralBaseController GetNeutralControllerForArea(PlayerArea area)
    {
        if (area == null || area.tableVisual == null)
            return null;
        NeutralBaseController[] allControllers = GameObject.FindObjectsByType<NeutralBaseController>(FindObjectsSortMode.None);
        foreach (NeutralBaseController c in allControllers)
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

    private bool PlayerOwnsBaseInController(NeutralBaseController controller)
    {
        if (controller == null) return false;
        OneBuildingManager[] allBuildings = GameObject.FindObjectsByType<OneBuildingManager>(FindObjectsSortMode.None);
        foreach (OneBuildingManager b in allBuildings)
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
        NeutralBaseController c = GetNeutralControllerForArea(area);
        if (c == null) return false;
        return PlayerOwnsBaseInController(c); // tag joueur + même controller
    }
    
    public void CalculatePlayerIncome()
    {
        playerMainIncome = 0;
        playerSecondIncome = 0;
        foreach (BaseAsset baseAsset in controlledBases)
        {
            playerMainIncome += baseAsset.mainRessourceIncome;
            playerSecondIncome += baseAsset.secondRessourceIncome;
        }
        baseVisual.ApplyLookFromAsset();
    }

    // METHODS TO CREATE A NEW BASE 
    // 1st overload - by ID
    public void CreateANewNeutralBase( BaseAsset baseAsset, NeutralBaseVisual neutralBaseVisual, NeutralBaseController neutralBaseController)
    {
        if (TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
        {
            new ShowMessageCommand("You can't do that right now", 2f).AddToQueue();
            return;
        }

        foreach (TableVisual table in neutralBaseController.tables)
        {
            if (table.tag == this.tag)
            {
                if (table.CreaturesOnTable.Count <= 0)
                {
                    new ShowMessageCommand("You need to have at least one creature on the selected table to build a base", 2f).AddToQueue();
                    return;
                }
            }
        }

        if (MainRessourceAvailable < baseAsset.mainRessourceBuildingCost || 
        SecondRessourceAvailable < baseAsset.secondRessourceBuildingCost)
        {
            new ShowMessageCommand("Insufficient Ressources", 2f).AddToQueue();
            return;
        }
        
        BuildingLogic newBuilding = new BuildingLogic(this, baseAsset, neutralBaseController);
        new BuildNeutralBaseCommand(newBuilding.UniqueBuildingID, this, neutralBaseVisual, baseAsset, neutralBaseController).AddToQueue();
        FogOfWarManager.Refresh();
    }


}
