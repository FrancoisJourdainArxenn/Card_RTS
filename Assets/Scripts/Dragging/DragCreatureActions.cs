using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

public class DragCreatureActions : DraggingActions {

    // reference to the sprite with a round "Target" graphic
    private SpriteRenderer sr;
    // reference to WhereIsTheCardOrCreature to track this object`s state in the game
    private WhereIsTheCardOrCreature whereIsThisCreature;
    // Reference to creature manager, attached to the parent game object
    private OneCreatureManager manager;
    private IDHolder idHolder;
    private Vector3 originalLocalPosition;


    [SerializeField] private CurvedArrow targettingArrow;
    // Prefab carte affiché sous la souris pendant le drag (à assigner dans l'Inspector)
    // [SerializeField] private GameObject dragCardPrefab;
    // [SerializeField] private Vector2 dragCardOffset = new Vector2(0f, 0f);
    // [SerializeField] private float dragCardScale = 0.3f;
    // private GameObject _dragCard;
    // private RectTransform _dragCardRect;
    // private Camera _dragUiCamera;
    // // Partagé entre toutes les créatures — créé au premier drag, détruit avec la scène
    // private static RectTransform _sharedDragCardParent;
    // private static Canvas _preferredDragCanvas;

    [SerializeField] private float _elevationHeight = 2f;
    private Vector3 _originalManagerLocalPosition;
    private bool _reorderDone = false;
    private bool _wasInOriginArea = true;



    // public static void SetDragCanvas(Canvas canvas) => _preferredDragCanvas = canvas;

    void Awake()
    {
        // establish all the connections
        sr = GetComponent<SpriteRenderer>();
        manager = GetComponentInParent<OneCreatureManager>();
        whereIsThisCreature = GetComponentInParent<WhereIsTheCardOrCreature>();
        idHolder = GetComponentInParent<IDHolder>();
        originalLocalPosition = transform.localPosition;
    }

    public override bool CanDrag
    {
        get
        {   
            return (
                manager != null
                && base.CanDrag
                && (
                    manager.CanMoveNow
                    || manager.CanReorderNow
                )
            );
        }
    }

    private PlayerArea originArea;
    public override void OnStartDrag()
    {
        _wasInOriginArea = true;

        if (GetCreatureLogic() != null)
        {
            if (NetworkSessionData.IsNetworkSession)
                GameNetworkManager.Instance.CancelMoveCreatureServerRpc(idHolder.UniqueID, playerOwner.playerIndex);
            else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
                TurnManager.Instance.CancelSoloMove(idHolder.UniqueID);
        }
        manager.ClearPendingMoveArrow();

        originArea = playerOwner.SelectedPArea();
        whereIsThisCreature.VisualState = VisualStates.Dragging;
        
        // enable target graphic
        sr.enabled = true;
        if (manager.CanMoveNow)
            HighlightReachableAreas();
    
        
        _originalManagerLocalPosition = manager.transform.localPosition;
        manager.transform.position += Vector3.up * _elevationHeight;
        // manager.SetVisible(false);
        // SpawnDragCard();
    }

    // private void SpawnDragCard()
    // {
    //     if (dragCardPrefab == null)
    //         return;

    //     RectTransform parent = GetOrCreateDragCardParent();
    //     if (parent == null)
    //         return;

    //     _dragCard = Instantiate(dragCardPrefab, parent);
    //     _dragCardRect = _dragCard.GetComponent<RectTransform>();
    //     // Card_Preview a son ancre en top-center — on recentre pour que anchoredPosition = position locale de la souris
    //     _dragCardRect.anchorMin = new Vector2(0.5f, 0.5f);
    //     _dragCardRect.anchorMax = new Vector2(0.5f, 0.5f);
    //     _dragCard.transform.localScale = Vector3.one * dragCardScale;

    //     Canvas canvas = parent.GetComponentInParent<Canvas>();
    //     _dragUiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

    //     OneCardManager cardManager = _dragCard.GetComponent<OneCardManager>();
    //     if (cardManager != null)
    //     {
    //         cardManager.cardAsset = manager.cardAsset;
    //         cardManager.ReadCardFromAsset();

    //         CreatureLogic creatureLogic = GetCreatureLogic();
    //         if (creatureLogic != null)
    //             cardManager.OverrideStats(creatureLogic.Attack, creatureLogic.Health, creatureLogic.MaxHealth);
    //     }

    //     UpdateDragCardPosition();
    // }

    // private void UpdateDragCardPosition()
    // {
    //     if (_dragCardRect == null || _sharedDragCardParent == null)
    //         return;

    //     RectTransformUtility.ScreenPointToLocalPointInRectangle(
    //         _sharedDragCardParent,
    //         Input.mousePosition,
    //         _dragUiCamera,
    //         out Vector2 localPoint
    //     );
    //     _dragCardRect.anchoredPosition = localPoint + dragCardOffset;
    // }

    // private static RectTransform GetOrCreateDragCardParent()
    // {
    //     if (_sharedDragCardParent != null)
    //         return _sharedDragCardParent;

    //     Canvas best = _preferredDragCanvas;
    //     if (best == null)
    //     {
    //         foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
    //         {
    //             if (c.renderMode == RenderMode.WorldSpace)
    //                 continue;
    //             if (best == null || c.sortingOrder > best.sortingOrder)
    //                 best = c;
    //         }
    //     }
    //     if (best == null)
    //         return null;

    //     Transform existing = best.transform.Find("DragCardLayer");
    //     if (existing != null)
    //     {
    //         _sharedDragCardParent = existing.GetComponent<RectTransform>();

    //         return _sharedDragCardParent;
    //     }

    //     GameObject layer = new GameObject("DragCardLayer");
    //     layer.transform.SetParent(best.transform, false);
    //     RectTransform rt = layer.AddComponent<RectTransform>();
    //     rt.anchorMin = Vector2.zero;
    //     rt.anchorMax = Vector2.one;
    //     rt.offsetMin = Vector2.zero;
    //     rt.offsetMax = Vector2.zero;
    //     layer.transform.SetAsLastSibling();
    //     _sharedDragCardParent = rt;

    //     return _sharedDragCardParent;
    // }

    private CreatureLogic GetCreatureLogic()
    {
        if (idHolder == null)
            idHolder = GetComponentInParent<IDHolder>();
        if (idHolder == null) return null;
        CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out CreatureLogic creatureLogic);
        return creatureLogic;
    }

    public override void OnDraggingInUpdate()
    {
        bool isInOriginArea = originArea.tableVisual.CursorOverThisTable;

        if (isInOriginArea)
        {
            manager.transform.position = new Vector3(
                transform.position.x,
                manager.transform.position.y,
                transform.position.z
            );
            if (targettingArrow.enabled)
                targettingArrow.Hide();
            if (manager.CanReorderNow)
            {
                GameObject creatureGO = IDHolder.GetGameObjectWithID(idHolder.UniqueID);
                originArea.tableVisual.ShowMovePreview(creatureGO);
            }
        }
        else
        {
            if (_wasInOriginArea)
            {
                Vector3 worldOrigin = manager.transform.parent != null
                    ? manager.transform.parent.TransformPoint(_originalManagerLocalPosition)
                    : _originalManagerLocalPosition;
                manager.transform.DOKill();
                manager.transform.DOMove(worldOrigin, 0.3f).SetEase(Ease.OutQuad);
                if (manager.CanMoveNow)
                    targettingArrow.Show(worldOrigin);
            }
            originArea.tableVisual.ClearInsertPreview();
        }

        _wasInOriginArea = isInOriginArea;
    }


    public override void OnEndDrag()
    {
        TurnManager turnmanager = TurnManager.Instance;

        if (turnmanager.CurrentPhase == TurnManager.TurnPhases.Command) {
            PlayerArea selectedPArea = playerOwner.SelectedPArea();

            if (selectedPArea == originArea)
                Reorder();
            else if (manager.CanMoveNow)
            {
                bool moveValid = Move(selectedPArea);
                if (!moveValid)
                    OnDragFailed();
            }
            else
            {
                new ShowMessageCommand("Can't move now", 1f).AddToQueue();
                OnDragFailed();
            }
        }

        // return target and arrow to original position
        ResetDragElements();
    }

    private void Reorder()
    {
        _reorderDone = true;

        GameObject creatureGO = IDHolder.GetGameObjectWithID(idHolder.UniqueID);
        originArea.tableVisual.ReorderCreature(creatureGO);

        if (NetworkSessionData.IsNetworkSession)
        {
            int[] meleeIDs  = ExtractIDs(originArea.tableVisual.MeleeCreaturesOnTable);
            int[] rangedIDs = ExtractIDs(originArea.tableVisual.RangedCreaturesOnTable);
            GameNetworkManager.Instance.ReorderCreaturesServerRpc(
                playerOwner.playerIndex, originArea.baseID, meleeIDs, rangedIDs);
        }
    }

    private static int[] ExtractIDs(List<GameObject> list)
    {
        int[] ids = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            IDHolder holder = list[i] != null ? list[i].GetComponent<IDHolder>() : null;
            ids[i] = holder != null ? holder.UniqueID : -1;
        }
        return ids;
    }

    private bool Move(PlayerArea targetPlayerArea)
    {
        if (targetPlayerArea == null)
        {
            Debug.Log("target player area null");
            return false;
        }
    
        if (targetPlayerArea == originArea)
        {
            Debug.Log("target player area is the same as the player area");
            return false;
        }

        ZoneManager currentZone = originArea.parentZone;
        ZoneManager targetZone = targetPlayerArea.parentZone;
        Debug.Log($"[Move] current={currentZone?.name ?? "NULL"}, target={targetZone?.name ?? "NULL"}, path={currentZone?.GetPathTo(targetZone)?.Logic?.DisplayName ?? "NULL"}");

        if (currentZone != targetZone)
        {
            ZonePath path = currentZone.GetPathTo(targetZone);
            if (path == null || !path.Logic.CanTraverse(playerOwner, currentZone.Logic))
            {
                new ShowMessageCommand("Zone not in range", 1f).AddToQueue();
                return false;
            }
        }

        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null)
            return false;

        int tablePos = targetPlayerArea.tableVisual.TablePosForNewCreature(creatureLogic.IsMelee);

        if (NetworkSessionData.IsNetworkSession)
        {
            GameNetworkManager.Instance.MoveCreatureServerRpc(idHolder.UniqueID, targetPlayerArea.baseID, tablePos, playerOwner.playerIndex);
            manager.ShowPendingMoveArrow(targetPlayerArea.transform.position);
        }
        else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
        {
            TurnManager.Instance.EnqueueSoloMove(idHolder.UniqueID, targetPlayerArea.baseID, tablePos);
            manager.ShowPendingMoveArrow(targetPlayerArea.transform.position);
        }
        else
        {
            creatureLogic.Move(targetPlayerArea.baseID, tablePos);
        }
        return true;

    }

    private void ResetDragElements()
    {
        whereIsThisCreature.VisualState = tag.Contains("Low") ? VisualStates.LowTable : VisualStates.TopTable;
        whereIsThisCreature.SetTableSortingOrder();

        // ResetColorizeUnits();
        manager.SetVisible(true);
        manager.UpdateGlow();
        TurnManager.RefreshAllPlayableHighlights();
        ResetAreaHighlights();
        originArea.tableVisual.ClearInsertPreview();

        if (!_reorderDone)
        {
            Vector3 worldOrigin = manager.transform.parent != null
                ? manager.transform.parent.TransformPoint(_originalManagerLocalPosition)
                : _originalManagerLocalPosition;
            manager.transform.DOKill();
            manager.transform.DOMove(worldOrigin, 0.3f).SetEase(Ease.OutQuad);
        }
        _reorderDone = false;

        transform.SetLocalPositionAndRotation(originalLocalPosition, Quaternion.Euler(90f, 0f, 0f));
        sr.enabled = false;
        targettingArrow.Hide();

        // if (_dragCard != null)
        // {
        //     Destroy(_dragCard);
        //     _dragCard = null;
        //     _dragCardRect = null;
        // }
    }

    private void OnDragFailed() { }

    private void HighlightReachableAreas()
    {
        // Debug.Log($"[Highlight] Phase={TurnManager.Instance.CurrentPhase}, originArea={originArea?.name ?? "NULL"}, parentZone={originArea?.parentZone?.name ?? "NULL"}, PAreas={playerOwner.PAreas?.Length}");

        if (TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
            return;
    
        if (originArea == null || originArea.parentZone == null)
            return;

        ZoneManager currentZone = originArea.parentZone;
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
        {
            if (pa == originArea) continue;
            if (!System.Array.Exists(playerOwner.PAreas, a => a == pa)) continue;
            ZonePath highlightPath = currentZone.GetPathTo(pa.parentZone);
            if (pa.parentZone == currentZone || (highlightPath != null && highlightPath.Logic.CanTraverse(playerOwner, currentZone.Logic)))
                pa.tableVisual.SetHighlight(true);
        }
    }

    private void ResetAreaHighlights()
    {
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
            pa.tableVisual.SetHighlight(false);
    }
    // NOT USED IN THIS SCRIPT
    protected override bool DragSuccessful()
    {
        return true;
    }
}
