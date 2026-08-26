using UnityEngine;
using System.Collections;
using DG.Tweening;

public class DragSpellNoTarget: DraggingActions{

    private int savedHandSlot;
    private WhereIsTheCardOrCreature whereIsCard;
    private OneCardManager manager;
    private IDHolder idHolder;

    [SerializeField] private float dragScale = 0.3f;
    // Distance (monde) au-delà de laquelle un relâchement joue le sort plutôt que de l'annuler.
    [SerializeField] private float cancelDistance = 1.5f;
    private Vector3 _originalScale;
    private Vector3 _originalWorldPos;

    public override bool CanDrag
    {
        get
        {
            // TODO : include full field check
            return base.CanDrag && manager.CanBeDraggedNow && !OnPlayTargetingSession.IsActive;
        }
    }

    void Awake()
    {
        whereIsCard = GetComponent<WhereIsTheCardOrCreature>();
        manager = GetComponent<OneCardManager>();
        idHolder = GetComponent<IDHolder>();
    }

    public override void OnStartDrag()
    {
        savedHandSlot = whereIsCard.Slot;

        whereIsCard.VisualState = VisualStates.Dragging;
        whereIsCard.BringToFront();

        _originalScale = transform.localScale;
        _originalWorldPos = transform.position;
        transform.DOScale(_originalScale * dragScale, 0.0f).SetEase(Ease.OutQuad);
    }

    public override void OnDraggingInUpdate()
    {
        // Clic droit = annulation immédiate, même si le clic gauche est toujours enfoncé (le drag
        // "souris" ne dépend que du clic gauche — voir Draggable — donc on le coupe nous-mêmes ici).
        if (Input.GetMouseButtonDown(1))
            Draggable.CancelCurrentDrag();
    }

    public override void OnEndDrag()
    {
        // Comme DragCreatureOnTable : on restaure l'échelle inconditionnellement ici, avant de
        // savoir si le drag a réussi — sinon un sort joué avec succès resterait rétréci (à
        // dragScale) pendant toute son animation de "carte jouée" vers PlayPreviewSpot.
        transform.localScale = _originalScale;

        CardHoldSlotVisual targetSlot = CardHoldSlotVisual.SlotUnderCursor;
        if (targetSlot != null)
        {
            if (targetSlot.TryHoldCard(gameObject, playerOwner))
                return;
            ReturnToHand();
            return;
        }

        if (DragSuccessful())
        {
            if (whereIsCard.HoldSlot != null)
            {
                whereIsCard.HoldSlot.ReleaseCard(gameObject);
                whereIsCard.HoldSlot = null;
            }
            GetComponent<Draggable>().enabled = false;
            PlaySpell();
        }
        else
            ReturnToHand();
    }

    // Annulation programmatique (Echap) pendant un drag en cours — voir Draggable.CancelCurrentDrag.
    public override void OnDragCancelled()
    {
        ReturnToHand();
    }

    private void PlaySpell()
    {
        if (idHolder == null)
            idHolder = GetComponent<IDHolder>();

        int cardID = idHolder.UniqueID;

        if (NetworkSessionData.IsNetworkSession)
        {
            int playerIndex = System.Array.IndexOf(Player.Players, playerOwner);
            GameNetworkManager.Instance.PlaySpellServerRpc(
                playerIndex, cardID, System.Array.Empty<int>(), System.Array.Empty<int>());
        }
        else
        {
            playerOwner.PlayASpellFromHand(cardID, -1);
        }
    }

    private void ReturnToHand()
    {
        transform.DOScale(_originalScale, 0.2f).SetEase(Ease.OutQuad);

        if (whereIsCard.HoldSlot != null)
        {
            whereIsCard.SetHoldSlotSortingOrder();
            transform.DOLocalMove(whereIsCard.HoldSlot.cardAnchor.localPosition, 0.3f);
            return;
        }

        // Set old sorting order
        whereIsCard.Slot = savedHandSlot;
        if (tag.Contains("Low"))
            whereIsCard.VisualState = VisualStates.LowHand;
        else
            whereIsCard.VisualState = VisualStates.TopHand;
        // Move this card back to its slot position
        HandVisual PlayerHand = playerOwner.handVisual;
        Vector3 oldCardPos = PlayerHand.slots.Children[savedHandSlot].transform.localPosition;
        transform.DOLocalMove(oldCardPos, 0.3f);
    }

    protected override bool DragSuccessful()
    {
        if (Vector3.Distance(transform.position, _originalWorldPos) <= cancelDistance)
            return false;

        // Le drag est autorisé même sans assez de ressource (voir OneCardManager.CanBeDraggedNow) —
        // pour pouvoir déposer la carte dans un CardHoldSlotVisual malgré tout. On revalide donc le
        // coût ici, au moment de jouer réellement le sort.
        if (idHolder == null)
            idHolder = GetComponent<IDHolder>();
        if (!CardLogic.CardsCreatedThisGame.TryGetValue(idHolder.UniqueID, out CardLogic cl)
            || cl.MainCost > playerOwner.MainRessourceAvailable)
        {
            new ShowMessageCommand("Not enough resources", 2f).AddToQueue();
            return false;
        }
        return true;
    }


}
