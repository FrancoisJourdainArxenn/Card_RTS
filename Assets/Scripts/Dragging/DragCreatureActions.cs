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
    // Empêche ResetDragElements de ramener la créature à sa position d'avant-drag quand elle a déjà
    // été replacée ailleurs (reorder dans la même zone, ou déplacement vers une autre zone).
    private bool _skipSnapBack = false;
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
        _skipSnapBack = false;

        if (idHolder == null)
            idHolder = GetComponentInParent<IDHolder>();

        originArea = playerOwner.SelectedPArea();
        if (originArea == null)
        {
            CreatureLogic startDragCreatureLogic = GetCreatureLogic();
            if (startDragCreatureLogic != null)
                originArea = playerOwner.GetPlayerAreaByID(startDragCreatureLogic.BaseID);
        }
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
        if (manager.HasPendingMove)
        {
            CreatureLogic creatureLogic = GetCreatureLogic();
            if (creatureLogic != null && !originArea.tableVisual.RowHasSpace(creatureLogic.IsMelee))
            {
                new ShowMessageCommand("You can't control more units in that zone.", 1f).AddToQueue();
                return;
            }
            CancelPendingMove();
        }

        _skipSnapBack = true;

        GameObject creatureGO = IDHolder.GetGameObjectWithID(idHolder.UniqueID);
        originArea.tableVisual.ReorderCreature(creatureGO);

        // Reordonner N'IMPORTE QUELLE créature (ghost inclus) peut décaler la position relative de
        // TOUTES les créatures de la rangée — vraies créatures et ghosts de déplacement en attente
        // confondus (ex : plusieurs déplacements en cours vers cette zone après un multi-select).
        // On rediffuse donc l'ordre complet de la rangée à chaque reorder.
        BroadcastRowOrder(originArea);
    }

    // Annule le déplacement en attente de la créature en cours de drag (RPC serveur / buffer solo) et
    // nettoie son état visuel (flèche, ghost dans la zone cible, affichage en bout de rangée). Sans
    // effet si la créature n'a pas de déplacement en attente.
    private void CancelPendingMove()
    {
        if (NetworkSessionData.IsNetworkSession)
            GameNetworkManager.Instance.CancelMoveCreatureServerRpc(idHolder.UniqueID, playerOwner.playerIndex);
        else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
            TurnManager.Instance.CancelSoloMove(idHolder.UniqueID);

        originArea.tableVisual.ClearPendingMoveRowEnd(manager.gameObject);
        manager.DestroyPendingMoveGhost();
        manager.ClearPendingMoveArrow();
    }

    // Diffuse l'ordre complet (IDs permanents, vraies créatures et ghosts de déplacement en attente
    // mélangés — voir TableVisual.PermanentIDOf) de chaque rangée de area : au serveur puis à tous
    // les clients en réseau (relayé via le mécanisme existant de reorder de créatures réelles), ou
    // directement en local en solo différé (aucun autre client à informer). Appelé à chaque
    // événement qui change la composition ou l'ordre visuel d'une rangée côté joueur local : spawn
    // d'un nouveau ghost (Move) et reorder par drag (Reorder).
    private void BroadcastRowOrder(PlayerArea area)
    {
        int[] meleeIDs  = ExtractIDs(area.tableVisual.MeleeCreaturesOnTable);
        int[] rangedIDs = ExtractIDs(area.tableVisual.RangedCreaturesOnTable);

        if (NetworkSessionData.IsNetworkSession)
            GameNetworkManager.Instance.ReorderCreaturesServerRpc(playerOwner.playerIndex, area.baseID, meleeIDs, rangedIDs);
        else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
            area.tableVisual.ApplyCreatureOrder(meleeIDs, rangedIDs);
    }

    private static int[] ExtractIDs(List<GameObject> list)
    {
        int[] ids = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
            ids[i] = TableVisual.PermanentIDOf(list[i]);
        return ids;
    }

    // explicitNetworkTablePos : utilisé par les déplacements groupés (multi-select), qui appellent Move
    // plusieurs fois de suite dans la même frame pour la même zone cible. Sans ça, chaque appel
    // rééchantillonnerait Input.mousePosition contre une rangée dont le compte grandit à chaque
    // itération (un ghost de plus par unité déjà traitée) — la même position de souris ne mappe alors
    // plus au même index relatif d'une unité à l'autre, et l'ordre du groupe devient imprévisible.
    // Passer un slot logique explicite, assigné une fois par l'appelant, élimine cette dépendance.
    private bool Move(PlayerArea targetPlayerArea, bool silent = false, int? explicitNetworkTablePos = null)
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
        // Debug.Log($"[Move] current={currentZone?.name ?? "NULL"}, target={targetZone?.name ?? "NULL"}, path={currentZone?.GetPathTo(targetZone)?.Logic?.DisplayName ?? "NULL"}");

        if (currentZone != targetZone)
        {
            ZonePath path = currentZone.GetPathTo(targetZone);
            if (path == null || !path.Logic.CanTraverse(playerOwner, currentZone.Logic))
            {
                if (!silent)
                    new ShowMessageCommand("Zone not in range", 1f).AddToQueue();
                return false;
            }
        }

        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null)
            return false;

        if (!targetPlayerArea.tableVisual.RowHasSpace(creatureLogic.IsMelee))
        {
            if (!silent)
                new ShowMessageCommand("You can't control more units in that zone.", 1f).AddToQueue();
            return false;
        }

        CancelPendingMove();

        // tablePos : index visuel brut (compte les ghosts locaux), utilisé pour placer le ghost
        // exactement là où la souris pointe. networkTablePos : équivalent "logique" (ghosts exclus),
        // seul index qui garde le même sens une fois envoyé sur le réseau / bufferisé (voir
        // TableVisual.ToNetworkTablePos).
        int tablePos, networkTablePos;
        if (explicitNetworkTablePos.HasValue)
        {
            networkTablePos = explicitNetworkTablePos.Value;
            tablePos = targetPlayerArea.tableVisual.FromNetworkTablePos(creatureLogic.IsMelee, networkTablePos);
        }
        else
        {
            tablePos = targetPlayerArea.tableVisual.TablePosForNewCreature(creatureLogic.IsMelee);
            networkTablePos = targetPlayerArea.tableVisual.ToNetworkTablePos(creatureLogic.IsMelee, tablePos);
        }

        if (NetworkSessionData.IsNetworkSession)
        {
            GameNetworkManager.Instance.MoveCreatureServerRpc(idHolder.UniqueID, targetPlayerArea.baseID, networkTablePos, playerOwner.playerIndex);
            manager.ShowPendingMoveArrow(targetPlayerArea.transform.position);
            originArea.tableVisual.MarkPendingMoveAtRowEnd(manager.gameObject);
            SpawnPendingMoveGhost(creatureLogic, targetPlayerArea, tablePos);
            BroadcastRowOrder(targetPlayerArea);
        }
        else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
        {
            TurnManager.Instance.EnqueueSoloMove(idHolder.UniqueID, targetPlayerArea.baseID, networkTablePos);
            manager.ShowPendingMoveArrow(targetPlayerArea.transform.position);
            originArea.tableVisual.MarkPendingMoveAtRowEnd(manager.gameObject);
            SpawnPendingMoveGhost(creatureLogic, targetPlayerArea, tablePos);
            BroadcastRowOrder(targetPlayerArea);
        }
        else
        {
            creatureLogic.Move(targetPlayerArea.baseID, tablePos);
        }
        _skipSnapBack = true;
        return true;

    }

    // Créature "placeholder" grisée dans la zone cible, représentant l'unité en attente de déplacement.
    // Réordonnable dans sa rangée (voir Reorder()) pour laisser le joueur choisir sa place d'arrivée.
    private void SpawnPendingMoveGhost(CreatureLogic creatureLogic, PlayerArea targetArea, int tablePos)
    {
        manager.DestroyPendingMoveGhost(); // sécurité : jamais deux ghosts pour le même déplacement

        int ghostID = IDFactory.GetLocalOnlyID();
        targetArea.tableVisual.AddCreatureAtIndex(creatureLogic.ca, ghostID, tablePos, targetArea.baseID, completeCommand: false);

        GameObject ghostGO = IDHolder.GetGameObjectWithID(ghostID);
        if (ghostGO == null) return;

        OneCreatureManager ghostManager = ghostGO.GetComponent<OneCreatureManager>();
        ghostManager.IsPendingMoveGhost = true;
        ghostManager.PendingMoveSourceCreatureID = idHolder.UniqueID;
        ghostManager.PendingMoveOrigin = manager;
        ghostManager.CanReorderNow = true;
        ghostManager.CanMoveNow = false;
        ghostManager.UpdateGlow();

        manager.PendingMoveGhost = ghostGO;
        manager.SetPending(true, isPendingMove: true); // la carte d'origine s'assombrit et affiche l'icône tant que le déplacement est en attente ; le ghost reste net, sans icône
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

        if (!_skipSnapBack)
        {
            Vector3 worldOrigin = manager.transform.parent != null
                ? manager.transform.parent.TransformPoint(_originalManagerLocalPosition)
                : _originalManagerLocalPosition;
            manager.transform.DOKill();
            manager.transform.DOMove(worldOrigin, 0.3f).SetEase(Ease.OutQuad);
        }
        _skipSnapBack = false;

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

    public void GetReachableAreasInto(HashSet<PlayerArea> result)
    {
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
            return;

        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null) return;

        PlayerArea origin = playerOwner.GetPlayerAreaByID(creatureLogic.BaseID);
        if (origin == null || origin.parentZone == null) return;

        ZoneManager currentZone = origin.parentZone;
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
        {
            if (pa == origin) continue;
            if (!System.Array.Exists(playerOwner.PAreas, a => a == pa)) continue;
            ZonePath path = currentZone.GetPathTo(pa.parentZone);
            if (pa.parentZone == currentZone || (path != null && path.Logic.CanTraverse(playerOwner, currentZone.Logic)))
                result.Add(pa);
        }
    }

    // explicitNetworkTablePos : slot logique (ghost-free) assigné par l'appelant (voir
    // MultiSelectionManager.ConfirmGroupMove) plutôt que rééchantillonné depuis la souris — nécessaire
    // dès que plusieurs unités sont déplacées vers la même zone dans la même frame (voir Move()).
    public bool TryGroupMoveTo(PlayerArea targetPlayerArea, int explicitNetworkTablePos)
    {
        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null) return false;

        originArea = playerOwner.GetPlayerAreaByID(creatureLogic.BaseID);
        if (originArea == null) return false;

        return Move(targetPlayerArea, silent: true, explicitNetworkTablePos: explicitNetworkTablePos);
    }

}
