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
            return base.CanDrag && manager.CanBePlayedNow && !OnPlayTargetingSession.IsActive;
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

    }

    public override void OnEndDrag()
    {
        if (DragSuccessful())
            PlaySpell();
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
        return Vector3.Distance(transform.position, _originalWorldPos) > cancelDistance;
    }


}
