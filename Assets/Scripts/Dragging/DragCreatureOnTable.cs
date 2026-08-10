using UnityEngine;
using System.Collections;
using DG.Tweening;

public class DragCreatureOnTable : DraggingActions {

    private int savedHandSlot;
    private WhereIsTheCardOrCreature whereIsCard;
    private IDHolder idScript;
    private VisualStates tempState;
    private OneCardManager manager;
    private bool _isReturning = false;
    private bool _isPlayed = false;


    [SerializeField] private float dragScale = 0.3f;
    private Vector3 _originalScale;
    private TableVisual _previewTable;

    public override bool CanDrag
    {
        get
        {
            return base.CanDrag && manager.CanBePlayedNow && !_isReturning && !_isPlayed;
        }
    }



    void Awake()
    {
        whereIsCard = GetComponent<WhereIsTheCardOrCreature>();
        manager = GetComponent<OneCardManager>();
    }

    public override void OnStartDrag()
    {
        savedHandSlot = whereIsCard.Slot;
        tempState = whereIsCard.VisualState;
        whereIsCard.VisualState = VisualStates.Dragging;
        whereIsCard.BringToFront();
        _originalScale = transform.localScale;
        transform.DOScale(_originalScale * dragScale, 0.0f).SetEase(Ease.OutQuad);
        HighlightValidAreas();
    }

    public override void OnDraggingInUpdate()
    {
        UpdateInsertPreview();
    }

    public override void OnEndDrag()
    {
        ClearInsertPreview();
        transform.localScale = _originalScale;
        ResetAreaHighlights();
        // 1) Check if we are holding a card over the table
        bool dragOk = DragSuccessful();
        if (dragOk)
        {
            _isPlayed = true;
            PlayerArea selectedPArea = playerOwner.SelectedPArea();
            bool isMelee = manager.cardAsset.melee;
            // Index visuel brut → index logique (ghosts exclus) : c'est ce dernier qui doit transiter
            // par le réseau / le buffer différé pour garder le même sens sur tous les clients
            // (voir TableVisual.ToNetworkTablePos).
            int visualTablePos = selectedPArea.tableVisual.TablePosForNewCreature(isMelee);
            int tablePos = selectedPArea.tableVisual.ToNetworkTablePos(isMelee, visualTablePos);


            if (NetworkSessionData.IsNetworkSession)
            {
                int playerIndex = System.Array.IndexOf(Player.Players, playerOwner);
                GameNetworkManager.Instance.PlayCreatureServerRpc(
                    GetComponent<IDHolder>().UniqueID,
                    tablePos,
                    selectedPArea.baseID,
                    playerIndex
                );
            }
            else
            {
                playerOwner.PlayACreatureFromHand(GetComponent<IDHolder>().UniqueID, tablePos, selectedPArea);
            }
             GetComponent<Draggable>().enabled = false;
        }
        else
        {
            DragFailed();
        }
    }

    protected override bool DragSuccessful()
    {
        if (!TableVisual.CursorOverSomeTable)
            return false;

        PlayerArea selectedPArea = playerOwner.SelectedPArea();
        if (!playerOwner.CanPlayCreatureInArea(selectedPArea, manager.cardAsset))
        {
            new ShowMessageCommand("You don't control a base in this zone", 2f).AddToQueue();
            return false;
        }
        bool RowNotFull = selectedPArea.tableVisual.RowHasSpace(manager.cardAsset.melee);
        if (!RowNotFull)
        {
            new ShowMessageCommand("You can't control more units in that zone.", 2f).AddToQueue();
            return false;
        }
        return true;
    }

    private void DragFailed()
    {
        StartCoroutine(ReturnToHand());
    }

    private IEnumerator ReturnToHand()
    {
        _isReturning = true;
        whereIsCard.SetHandSortingOrder();
        whereIsCard.VisualState = tempState;
        HandVisual PlayerHand = playerOwner.handVisual;
        Vector3 oldCardPos = PlayerHand.slots.Children[savedHandSlot].transform.localPosition;
        transform.DOLocalMove(oldCardPos, 0.3f);
        transform.DOScale(_originalScale, 0.3f).SetEase(Ease.OutQuad);
        yield return new WaitForSeconds(0.3f);
        _isReturning = false;
    }

    private void UpdateInsertPreview()
    {
        PlayerArea selectedPArea = playerOwner.SelectedPArea();
        if (selectedPArea == null || !TableVisual.CursorOverSomeTable
            || !playerOwner.CanPlayCreatureInArea(selectedPArea, manager.cardAsset))
        {
            ClearInsertPreview();
            return;
        }

        bool isMelee = manager.cardAsset.melee;
        int tablePos = selectedPArea.tableVisual.TablePosForNewCreature(isMelee);

        if (_previewTable != selectedPArea.tableVisual)
        {
            ClearInsertPreview();
            _previewTable = selectedPArea.tableVisual;
        }
        _previewTable.ShowInsertPreview(tablePos, isMelee);
    }

    private void ClearInsertPreview()
    {
        _previewTable?.ClearInsertPreview();
        _previewTable = null;
    }
    
    private void HighlightValidAreas()
    {
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
        {
            if (playerOwner.CanPlayCreatureInArea(pa, manager.cardAsset))
                pa.tableVisual.SetHighlight(true);
        }
    }

    private void ResetAreaHighlights()
    {
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
            pa.tableVisual.SetHighlight(false);
    }

}
