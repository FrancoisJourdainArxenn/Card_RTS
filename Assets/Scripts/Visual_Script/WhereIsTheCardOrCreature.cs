using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// an enum to store the info about where this object is
public enum VisualStates
{
    Transition,
    LowHand, 
    TopHand,
    LowTable,
    TopTable,
    Dragging
}

public class WhereIsTheCardOrCreature : MonoBehaviour {

    // Non-null quand cette carte repose dans un CardHoldSlotVisual plutôt qu'un slot de main
    // normal — utilisé par les scripts de drag pour savoir où la faire revenir en cas d'échec.
    public CardHoldSlotVisual HoldSlot;

    // reference to a HoverPreview Component
    private HoverPreview hover;

    // reference to a canvas on this object to set sorting order
    private Canvas canvas;

    // a value for canvas sorting order when we want to show this object above everything
    private int TopSortingOrder = 500;

    // PROPERTIES
    private int slot = -1;
    public int Slot
    {
        get{ return slot;}

        set
        {
            slot = value;
            /*if (value != -1)
            {
                canvas.sortingOrder = HandSortingOrder(slot);
            }*/
        }
    }

    private VisualStates state;
    public VisualStates VisualState
    {
        get{ return state; }  

        set
        {
            state = value;
            switch (state)
            {
                case VisualStates.LowHand:
                    hover.ThisPreviewEnabled = true;
                    break;
                case VisualStates.LowTable:
                case VisualStates.TopTable:
                    hover.ThisPreviewEnabled = true; 
                    break;
                case VisualStates.Transition:
                    hover.ThisPreviewEnabled = false;
                    break;
                case VisualStates.Dragging:
                    hover.ThisPreviewEnabled = false;
                    break;
                case VisualStates.TopHand:
                    hover.ThisPreviewEnabled = true;
                    break;
            }
        }
    }

    void Awake()
    {
        hover = GetComponent<HoverPreview>();
        // for characters hover is attached to a child game object
        if (hover == null)
            hover = GetComponentInChildren<HoverPreview>();
        canvas = GetComponentInChildren<Canvas>();
    }

    public void BringToFront()
    {
        canvas.sortingOrder = TopSortingOrder;
        canvas.sortingLayerName = "AboveEverything";
    }

    // not setting sorting order inside of VisualStaes property because when the card is drawn,
    // we want to set an index first and set the sorting order only when the card arrives to hand.
    public void SetHandSortingOrder()
    {
        if (slot != -1)
            canvas.sortingOrder = HandSortingOrder(slot);
        canvas.sortingLayerName = "Cards";
    }

    // Utilisé pour une carte posée dans un CardHoldSlotVisual : son `slot` de main est obsolète
    // à ce stade (la carte vient d'en être retirée), et HandSortingOrder(slot) reste de toute
    // façon négatif — or Card_Hold.prefab (le visuel du slot) est sur ce même layer "Cards" avec
    // un sortingOrder de 2, donc rendu au-dessus de toute carte en main normale. On force ici un
    // ordre positif garanti supérieur, pour que la carte tenue reste visible par-dessus.
    private const int HoldSlotSortingOrder = 10;
    public void SetHoldSlotSortingOrder()
    {
        canvas.sortingOrder = HoldSlotSortingOrder;
        canvas.sortingLayerName = "Cards";
    }

    public void SetTableSortingOrder()
    {
        canvas.sortingOrder = 0;
        canvas.sortingLayerName = "Units";
    }

    // Cache/montre uniquement le visuel de la carte (son Canvas) sans désactiver le GameObject :
    // l'IDHolder reste résolvable et tout enfant hors-Canvas (ex: une flèche de ciblage) reste actif.
    public void SetFaceVisible(bool visible)
    {
        if (canvas != null)
            canvas.enabled = visible;
    }

    private int HandSortingOrder(int placeInHand)
    {
        return (-(placeInHand + 1) * 10); 
    }


}
