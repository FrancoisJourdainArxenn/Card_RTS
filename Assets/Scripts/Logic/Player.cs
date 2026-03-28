using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

public class Player : MonoBehaviour, ICharacter
{
    // PUBLIC FIELDS
    // int ID that we get from ID factory
    public int PlayerID;
    // a Character Asset that contains data about this Hero
    public FactionAsset factionAsset;
    public BaseAsset baseAsset;

    // a script with references to all the visual game objects for this player
    public PlayerArea[] PAreas;
    public PlayerArea MainPArea = null;
    public BaseVisual baseVisual;
    public Color playerColor;

    public int mainRessourceTotal;
    public int mainRessourceAvailable;
    public int secondRessourceTotal;
    public int secondRessourceAvailable;

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
    // this property is a part of interface ICharacter
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
        // obtain unique id from IDFactory
        PlayerID = IDFactory.GetUniqueID();
    }

    public virtual void OnTurnStart()
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
            mainRessourceAvailable += baseAsset.mainRessourceIncome;
        }
        if (secondRessourceAvailable >= secondRessourceTotal)
            secondRessourceAvailable = secondRessourceTotal;
        else
        {
            secondRessourceAvailable += baseAsset.secondRessourceIncome;
        }

        
        // Refresh UI + playable state.
        HighlightPlayableCards();
        if (baseVisual != null)
            baseVisual.ApplyLookFromAsset();

        if (table != null)
        {
            foreach (CreatureLogic cl in table.CreaturesOnTable)
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

    // 2nd overload - takes CardLogic and ICharacter interface - 
    // this method is called from Logic, for example by AI
    public void PlayASpellFromHand(CardLogic playedCard, ICharacter target)
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
        CreatureLogic newCreature = new CreatureLogic(this, playedCard.ca);
        table.CreaturesOnTable.Insert(tablePos, newCreature);
        // 
        new PlayACreatureCommand(playedCard, this, tablePos, newCreature.UniqueCreatureID, selectedPArea).AddToQueue();
        // cause battlecry Effect
        if (newCreature.effect != null)
            newCreature.effect.WhenACreatureIsPlayed();
        // remove this card from hand
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
        foreach (CreatureLogic crl in table.CreaturesOnTable)
        {
            GameObject g = IDHolder.GetGameObjectWithID(crl.UniqueCreatureID);
            if (g != null)
                g.GetComponent<OneCreatureManager>().CanAttackNow = canAttack && (crl.AttacksLeftThisTurn > 0) && !crl.Frozen && !removeAllHighlights;
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



}
