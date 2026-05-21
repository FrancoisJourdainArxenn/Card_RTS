using UnityEngine;
using System.Collections;
using DG.Tweening;

public class DragCreatureOnTable : DraggingActions {

    private int savedHandSlot;
    private WhereIsTheCardOrCreature whereIsCard;
    private IDHolder idScript;
    private VisualStates tempState;
    private OneCardManager manager;
    public int maxCreatureOnBoard = 7;
    private bool _isReturning = false;


    [SerializeField] private float dragScale = 0.3f;
    private Vector3 _originalScale;
    private TableVisual _previewTable;

    public override bool CanDrag
    {
        get
        { 
            return base.CanDrag && manager.CanBePlayedNow && !_isReturning;
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
        if (DragSuccessful())
        {
            PlayerArea selectedPArea = playerOwner.SelectedPArea();
            bool isMelee = manager.cardAsset.melee;
            float dropDepth = selectedPArea.tableVisual.GetRowWorldZ(isMelee) - Camera.main.transform.position.z;
            int tablePos = selectedPArea.tableVisual.TablePosForNewCreature(
                Camera.main.ScreenToWorldPoint(new Vector3(
                    Input.mousePosition.x, Input.mousePosition.y, dropDepth)).x,
                isMelee
            );


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
        }
        else
        {
            DragFailed();
        }
    }

    protected override bool DragSuccessful()
    {
        PlayerArea selectedPArea = playerOwner.SelectedPArea();
        if (!playerOwner.CanPlayCreatureInArea(selectedPArea))
        {
            new ShowMessageCommand("You don't control a base in this zone", 2f).AddToQueue();
            return false;
        }
        bool TableNotFull = (selectedPArea.tableVisual.TotalCreatureCount < maxCreatureOnBoard);
        if (!TableNotFull)
        {
            new ShowMessageCommand("You can't control more units in that zone.", 2f).AddToQueue();
            return false;
        }
        return TableVisual.CursorOverSomeTable && TableNotFull && selectedPArea != null;
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
            || !playerOwner.CanPlayCreatureInArea(selectedPArea))
        {
            ClearInsertPreview();
            return;
        }

        bool isMelee = manager.cardAsset.melee;
        float depth = selectedPArea.tableVisual.GetRowWorldZ(isMelee) - Camera.main.transform.position.z;
        float worldX = Camera.main.ScreenToWorldPoint(new Vector3(
            Input.mousePosition.x, Input.mousePosition.y, depth)).x;
        int tablePos = selectedPArea.tableVisual.TablePosForNewCreature(worldX, isMelee);

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
            if (playerOwner.CanPlayCreatureInArea(pa))
                pa.tableVisual.SetHighlight(true);
        }
    }

    private void ResetAreaHighlights()
    {
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
            pa.tableVisual.SetHighlight(false);
    }

}
