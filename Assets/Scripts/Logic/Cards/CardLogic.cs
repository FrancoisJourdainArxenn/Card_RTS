using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

[System.Serializable]
public class CardLogic: IIdentifiable
{
    // reference to a player who holds this card in his hand
    public Player owner;
    // an ID of this card
    public int UniqueCardID; 
    // a reference to the card asset that stores all the info about this card
    public CardAsset ca;
    // a script of type spell effect that will be attached to this card when it`s created



    // STATIC (for managing IDs)
    public static Dictionary<int, CardLogic> CardsCreatedThisGame = new Dictionary<int, CardLogic>();


    // PROPERTIES
    public int ID
    {
        get{ return UniqueCardID; }
    }

    public int MainCost{ get; set; }
    public int SecondCost{ get; set; }       
    public bool CanBePlayed
    {
        get
        {
            TurnManager tm = TurnManager.Instance;
            bool ownersTurn = tm != null
                && tm.IsCommandPhase
                && tm.MayPlayerUseControlsInPhase(owner);
            bool fieldNotFull = true;
            // but if this is a creature, we have to check if there is room on board (table)
            /*if (ca.MaxHealth > 0)
                fieldNotFull = (owner.table.CreaturesOnTable.Count < 7);*/
            //Debug.Log("Card: " + ca.name + " has params: ownersTurn=" + ownersTurn + "fieldNotFull=" + fieldNotFull + " hasMana=" + (CurrentManaCost <= owner.ManaLeft));
            return ownersTurn && fieldNotFull && (MainCost <= owner.MainRessourceAvailable) && (SecondCost <= owner.SecondRessourceAvailable);
        }
    }

    // CONSTRUCTOR
    public CardLogic(CardAsset ca, int networkID = -1)
    {
        // set the CardAsset reference
        this.ca = ca;
        // get unique int ID
        UniqueCardID = networkID != -1 ? networkID : IDFactory.GetUniqueID();
        //UniqueCardID = IDFactory.GetUniqueID();
        MainCost = ca.MainCost;
        SecondCost = ca.SecondCost;
        // if (ca.Effects != null && ca.Effects.Count > 0)
        //     EffectProcessor.RegisterCreatureEffects(this, ca);
        // add this card to a dictionary with its ID as a key
        CardsCreatedThisGame.Add(UniqueCardID, this);
    }

    // method to set or reset mana cost
    public void ResetCosts()
    {
        MainCost = ca.MainCost;
        SecondCost = ca.SecondCost;
    }

}
