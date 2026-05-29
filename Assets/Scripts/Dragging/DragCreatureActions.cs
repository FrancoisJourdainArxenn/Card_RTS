using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    [SerializeField] private GameObject dragCardPrefab;
    [SerializeField] private Vector2 dragCardOffset = new Vector2(60f, -80f);
    [SerializeField] private float dragCardScale = 0.3f;
    private GameObject _dragCard;
    private RectTransform _dragCardRect;
    private Camera _dragUiCamera;
    // Partagé entre toutes les créatures — créé au premier drag, détruit avec la scène
    private static RectTransform _sharedDragCardParent;

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
            return manager != null && base.CanDrag && manager.CanMoveNow;
        }
    }
    private PlayerArea originArea;
    public override void OnStartDrag()
    {
        if (NetworkSessionData.IsNetworkSession)
        {
            IDHolder idHolder = GetComponentInParent<IDHolder>();
            if (idHolder != null)
                GameNetworkManager.Instance.CancelMoveCreatureServerRpc(idHolder.UniqueID, playerOwner.playerIndex);
        }
        else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
        {
            IDHolder idHolder = GetComponentInParent<IDHolder>();
            if (idHolder != null)
                TurnManager.Instance.CancelSoloMove(idHolder.UniqueID);
        }
        manager.ClearPendingMoveArrow();

        originArea = playerOwner.SelectedPArea();
        whereIsThisCreature.VisualState = VisualStates.Dragging;
        // enable target graphic
        sr.enabled = true;
        HighlightReachableAreas();
        //ColorizeUnits();

        manager.SetVisible(false);
        SpawnDragCard();
    }

    private void SpawnDragCard()
    {
        if (dragCardPrefab == null)
            return;

        RectTransform parent = GetOrCreateDragCardParent();
        if (parent == null)
            return;

        _dragCard = Instantiate(dragCardPrefab, parent);
        _dragCardRect = _dragCard.GetComponent<RectTransform>();
        // Card_Preview a son ancre en top-center — on recentre pour que anchoredPosition = position locale de la souris
        _dragCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        _dragCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        _dragCard.transform.localScale = Vector3.one * dragCardScale;

        Canvas canvas = parent.GetComponentInParent<Canvas>();
        _dragUiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        OneCardManager cardManager = _dragCard.GetComponent<OneCardManager>();
        if (cardManager != null)
        {
            cardManager.cardAsset = manager.cardAsset;
            cardManager.ReadCardFromAsset();

            IDHolder ih = GetComponentInParent<IDHolder>();
            if (ih != null && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(ih.UniqueID, out CreatureLogic cl))
                cardManager.OverrideStats(cl.Attack, cl.Health, cl.MaxHealth);
        }

        UpdateDragCardPosition();
    }

    private void UpdateDragCardPosition()
    {
        if (_dragCardRect == null || _sharedDragCardParent == null)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _sharedDragCardParent,
            Input.mousePosition,
            _dragUiCamera,
            out Vector2 localPoint
        );
        _dragCardRect.anchoredPosition = localPoint + dragCardOffset;
    }

    private static RectTransform GetOrCreateDragCardParent()
    {
        if (_sharedDragCardParent != null)
            return _sharedDragCardParent;

        Canvas best = null;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c.renderMode == RenderMode.WorldSpace) continue;
            if (best == null || c.sortingOrder > best.sortingOrder)
                best = c;
        }
        if (best == null)
            return null;

        Transform existing = best.transform.Find("DragCardLayer");
        if (existing != null)
        {
            _sharedDragCardParent = existing.GetComponent<RectTransform>();

            return _sharedDragCardParent;
        }

        GameObject layer = new GameObject("DragCardLayer");
        layer.transform.SetParent(best.transform, false);
        RectTransform rt = layer.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        layer.transform.SetAsLastSibling();
        _sharedDragCardParent = rt;

        return _sharedDragCardParent;
    }

    public override void OnDraggingInUpdate()
    {
        UpdateDragCardPosition();
        bool isInOriginArea = originArea.tableVisual.CursorOverThisTable;

        if (isInOriginArea)
        {
            if (targettingArrow.enabled)
                targettingArrow.Hide();
            // IDHolder est ajouté via AddComponent après Instantiate, donc Awake() le rate — on le récupère ici au premier drag
            if (idHolder == null)
                idHolder = GetComponentInParent<IDHolder>();
            if (idHolder != null && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out CreatureLogic cl))
            {
                GameObject creatureGO = IDHolder.GetGameObjectWithID(idHolder.UniqueID);
                originArea.tableVisual.ShowMovePreview(creatureGO);
                // originArea.tableVisual.ShowInsertPreview(originArea.tableVisual.TablePosForNewCreature(cl.IsMelee), cl.IsMelee);
            }
        }
        else
        {
            if(!targettingArrow.enabled)
                targettingArrow.Show();
            originArea.tableVisual.ClearInsertPreview();
        }
    }

    public override void OnEndDrag()
    {
        TurnManager turnmanager = TurnManager.Instance;

        if (turnmanager.CurrentPhase == TurnManager.TurnPhases.Command) {
            PlayerArea selectedPArea = playerOwner.SelectedPArea();

            if (selectedPArea == originArea)
                Reorder();
            else
            {
                bool moveValid = Move(selectedPArea);
                if (!moveValid)
                    OnDragFailed();
            }
        }

        // return target and arrow to original position
        ResetDragElements();
    }

    private void Reorder()
    {
        IDHolder moverIdHolder = GetComponentInParent<IDHolder>();
        if (moverIdHolder == null || !CreatureLogic.CreaturesCreatedThisGame.ContainsKey(moverIdHolder.UniqueID))
            return;

        GameObject creatureGO = IDHolder.GetGameObjectWithID(moverIdHolder.UniqueID);
        originArea.tableVisual.ReorderCreature(creatureGO);
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

        IDHolder moverIdHolder = GetComponentInParent<IDHolder>();
        if (moverIdHolder == null)
        {
            // Debug.Log("pas d'ID pour le mover");
            return false;
        }

        if (!CreatureLogic.CreaturesCreatedThisGame.ContainsKey(moverIdHolder.UniqueID))
        {
            // Debug.Log("mover not found");
            return false;
        }
        bool isMelee = CreatureLogic.CreaturesCreatedThisGame[moverIdHolder.UniqueID].IsMelee;
        int tablePos = targetPlayerArea.tableVisual.TablePosForNewCreature(isMelee);

        if (NetworkSessionData.IsNetworkSession)
        {
            GameNetworkManager.Instance.MoveCreatureServerRpc(moverIdHolder.UniqueID, targetPlayerArea.baseID, tablePos, playerOwner.playerIndex);
            manager.ShowPendingMoveArrow(targetPlayerArea.transform.position);
        }
        else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
        {
            TurnManager.Instance.EnqueueSoloMove(moverIdHolder.UniqueID, targetPlayerArea.baseID, tablePos);
            manager.ShowPendingMoveArrow(targetPlayerArea.transform.position);
        }
        else
        {
            CreatureLogic.CreaturesCreatedThisGame[moverIdHolder.UniqueID].Move(targetPlayerArea.baseID, tablePos);
        }
        return true;

    }

    private void ResetDragElements()
    {
        // ResetColorizeUnits();
        ResetAreaHighlights();
        originArea.tableVisual.ClearInsertPreview();

        transform.SetLocalPositionAndRotation(originalLocalPosition, Quaternion.Euler(90f, 0f, 0f));
        sr.enabled = false;
        targettingArrow.Hide();

        manager.SetVisible(true);

        if (_dragCard != null)
        {
            Destroy(_dragCard);
            _dragCard = null;
            _dragCardRect = null;
        }
    }

    private void OnDragFailed()
    {
        {
            // not a valid target, return
            if (tag.Contains("Low"))
                whereIsThisCreature.VisualState = VisualStates.LowTable;
            else
                whereIsThisCreature.VisualState = VisualStates.TopTable;
            whereIsThisCreature.SetTableSortingOrder();
        }
    }

    /*private void ColorizeUnits()
    {
        TurnManager turnmanager = TurnManager.Instance;
        if (turnmanager.CurrentPhase != TurnManager.TurnPhases.Battle) {
            return;
        }
        foreach (CreatureLogic cl in playerOwner.otherPlayer.table.CreaturesInPlay)
        {
            GameObject g = IDHolder.GetGameObjectWithID(cl.UniqueCreatureID);
            g.GetComponent<OneCreatureManager>().UpdateTargetableVisual(cl.Targetable);
        }
    }*/

    /*private void ResetColorizeUnits()
    {
        foreach (CreatureLogic cl in playerOwner.otherPlayer.table.CreaturesInPlay)
        {
            GameObject g = IDHolder.GetGameObjectWithID(cl.UniqueCreatureID);
            g.GetComponent<OneCreatureManager>().UpdateTargetableVisual(true);
        }
    }*/

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
