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

    // Transports amis, dans la même zone, avec de la place — calculé une fois au début du drag (voir
    // HighlightBoardableTransports), puis survolé chaque frame (voir UpdateHoveredTransport). Faire
    // glisser cette créature sur l'un d'eux l'embarque au lieu de la déplacer vers une zone.
    private readonly List<OneCreatureManager> _candidateTransports = new List<OneCreatureManager>();
    private OneCreatureManager _hoveredTransport;
    // Non-null tant qu'un embarquement (pas un déplacement de zone classique) est en attente sur cette
    // créature — distingue les deux branches dans CancelPendingMove.
    private int? _pendingBoardTransportID;



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
                && !MultiSelectionManager.IsConfirmingGroupMove
                && PhaseEffectPipeline.IsPlayerTargetingComplete(playerOwner)
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
        {
            HighlightReachableAreas();
            HighlightBoardableTransports();
        }
    
        
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
        if (Input.GetMouseButtonDown(1))
        {
            Draggable.CancelCurrentDrag();
            return;
        }

        UpdateHoveredTransport();

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

            if (_hoveredTransport != null && manager.CanMoveNow)
            {
                bool boardValid = Board(_hoveredTransport);
                if (!boardValid)
                    OnDragFailed();
            }
            else if (selectedPArea == originArea)
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

        ClearTransportHighlights();
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

    // Annule le déplacement (ou embarquement) en attente de cette créature (RPC serveur / buffer
    // solo) et nettoie son état visuel (flèche, ghost dans la zone cible, affichage en bout de
    // rangée). Sans effet si la créature n'a pas de déplacement en attente.
    // checkCapacity : la créature n'a jamais quitté la liste de sa rangée d'origine pendant l'attente
    // (juste cachée/exclue du calcul de place via HasPendingMove — voir Board()/TableVisual.PlaceRowOnSlots),
    // donc une autre carte jouée entre-temps a pu légitimement prendre sa place. true pour une
    // annulation autonome (portrait cliqué — voir OneCreatureManager.RequestDisembarkPassenger, seul
    // appelant externe) où il faut vérifier avant de la réafficher ; false (défaut) pour les 3 appels
    // internes (Reorder/Move/Board ci-dessous) qui appellent ceci juste avant d'établir eux-mêmes un
    // nouvel état en attente — y bloquer la restauration laisserait un double état en attente au lieu
    // de remplacer proprement l'ancien.
    public void CancelPendingMove(bool checkCapacity = false)
    {
        if (_pendingBoardTransportID.HasValue)
        {
            CreatureLogic pendingBoardCreatureLogic = checkCapacity ? GetCreatureLogic() : null;
            if (checkCapacity && pendingBoardCreatureLogic != null && originArea != null
                && !originArea.tableVisual.RowHasSpace(pendingBoardCreatureLogic.IsMelee))
            {
                //Debug.Log($"[Transport] CancelPendingMove — abort, no room back in origin row for ID:{idHolder.UniqueID} (another card filled that slot meanwhile) — stays boarded");
                new ShowMessageCommand("No room to bring that unit back — it stays aboard.", 1f).AddToQueue();
                return;
            }

            //Debug.Log($"[Transport] CancelPendingMove — cancelling pending board of ID:{idHolder.UniqueID} onto transport ID:{_pendingBoardTransportID.Value}");
            if (NetworkSessionData.IsNetworkSession)
                GameNetworkManager.Instance.CancelBoardCreatureServerRpc(idHolder.UniqueID, playerOwner.playerIndex);
            else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
                TurnManager.Instance.CancelSoloBoard(idHolder.UniqueID);

            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(_pendingBoardTransportID.Value, out CreatureLogic transportLogic))
            {
                transportLogic.RemoveLocalPendingBoard(idHolder.UniqueID);
                // Contrairement à la résolution (Board(), qui laisse le passager au manifeste), une
                // annulation doit l'en retirer explicitement — voir CreatureLogic.RemoveLocalPendingBoard.
                transportLogic.RemoveFromManifest(idHolder.UniqueID);
                IDHolder.GetGameObjectWithID(_pendingBoardTransportID.Value)?.GetComponent<OneCreatureManager>()?.RefreshPassengerPortraits();
            }

            // Un embarquement n'a jamais de ghost (voir Board) — DestroyPendingMoveGhost() no-opterait
            // silencieusement (elle sort tôt si PendingMoveGhost est null) sans jamais restaurer la
            // couleur normale de la créature, contrairement au cas déplacement ci-dessous.
            manager.ClearPendingMoveArrow();
            manager.HasPendingBoard = false;
            manager.PendingBoardTarget = null;
            manager.SetPending(false);
            // Réaffiche la créature masquée au moment du drag (voir Board()) — l'embarquement en
            // attente est annulé, elle reste dans sa rangée d'origine. AVANT ClearPendingMoveRowEnd
            // (qui déclenche le relayout) : tant que HasPendingBoard est vrai, PlaceRowOnSlots
            // l'exclut du calcul de slots — l'y réintégrer après aurait laissé les autres créatures
            // déjà recentrées sans elle, et elle-même figée à sa dernière position connue (chevauchement).
            manager.gameObject.SetActive(true);
            originArea.tableVisual.ClearPendingMoveRowEnd(manager.gameObject);
            _pendingBoardTransportID = null;
            return;
        }

        // Même risque que ci-dessus pour un déplacement classique (voir le commentaire de checkCapacity
        // sur la signature de la méthode).
        CreatureLogic pendingMoveCreatureLogic = checkCapacity ? GetCreatureLogic() : null;
        if (checkCapacity && pendingMoveCreatureLogic != null && originArea != null
            && !originArea.tableVisual.RowHasSpace(pendingMoveCreatureLogic.IsMelee))
        {
            //Debug.Log($"[Transport] CancelPendingMove — abort, no room back in origin row for ID:{idHolder.UniqueID} (another card filled that slot meanwhile) — move stays pending");
            new ShowMessageCommand("No room to cancel that move — another unit took its place.", 1f).AddToQueue();
            return;
        }

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

        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null)
            return false;

        ZoneManager currentZone = originArea.parentZone;
        ZoneManager targetZone = targetPlayerArea.parentZone;

        // Non-null uniquement si le réseau de téléporteurs (et non un ZonePath physique) est ce qui
        // rend ce déplacement légal — voir ZoneManager.CanReach. Sert à faire faire à la flèche de
        // mouvement en attente un crochet par ces deux créatures (voir ShowPendingMoveArrowViaTeleporter).
        OneCreatureManager viaSourceTeleporter = null;
        OneCreatureManager viaDestTeleporter = null;

        if (currentZone != targetZone)
        {
            if (!currentZone.CanReach(targetZone, playerOwner, creatureLogic, out CreatureLogic sourceTeleporterLogic, out CreatureLogic destTeleporterLogic))
            {
                if (!silent)
                    new ShowMessageCommand("Zone not in range", 1f).AddToQueue();
                return false;
            }

            if (sourceTeleporterLogic != null && destTeleporterLogic != null)
            {
                viaSourceTeleporter = IDHolder.GetGameObjectWithID(sourceTeleporterLogic.UniqueCreatureID)?.GetComponent<OneCreatureManager>();
                viaDestTeleporter = IDHolder.GetGameObjectWithID(destTeleporterLogic.UniqueCreatureID)?.GetComponent<OneCreatureManager>();
            }
        }

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
            if (viaSourceTeleporter != null && viaDestTeleporter != null)
                manager.ShowPendingMoveArrowViaTeleporter(viaSourceTeleporter, viaDestTeleporter);
            else
                manager.ShowPendingMoveArrow(targetPlayerArea.transform.position);
            originArea.tableVisual.MarkPendingMoveAtRowEnd(manager.gameObject);
            SpawnPendingMoveGhost(creatureLogic, targetPlayerArea, tablePos);
            BroadcastRowOrder(targetPlayerArea);
        }
        else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
        {
            TurnManager.Instance.EnqueueSoloMove(idHolder.UniqueID, targetPlayerArea.baseID, networkTablePos);
            if (viaSourceTeleporter != null && viaDestTeleporter != null)
                manager.ShowPendingMoveArrowViaTeleporter(viaSourceTeleporter, viaDestTeleporter);
            else
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

    // Annulation programmatique (clic droit / Echap) pendant un drag en cours — voir
    // Draggable.CancelCurrentDrag. A ce stade, Move()/Reorder() n'ont pas encore été appelés (rien
    // n'est committé), donc _skipSnapBack est toujours false : ResetDragElements() ramène la
    // créature à sa position d'origine SUR LA TABLE (jamais en main, contrairement à DragCreatureOnTable).
    public override void OnDragCancelled()
    {
        ClearTransportHighlights();
        ResetDragElements();
    }

    private void HighlightReachableAreas()
    {
        // Debug.Log($"[Highlight] Phase={TurnManager.Instance.CurrentPhase}, originArea={originArea?.name ?? "NULL"}, parentZone={originArea?.parentZone?.name ?? "NULL"}, PAreas={playerOwner.PAreas?.Length}");

        if (TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
            return;
    
        if (originArea == null || originArea.parentZone == null)
            return;

        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null)
            return;

        ZoneManager currentZone = originArea.parentZone;
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
        {
            if (pa == originArea) continue;
            if (!System.Array.Exists(playerOwner.PAreas, a => a == pa)) continue;
            if (pa.parentZone == currentZone || currentZone.CanReach(pa.parentZone, playerOwner, creatureLogic))
                pa.tableVisual.SetHighlight(true);
        }
    }

    private void ResetAreaHighlights()
    {
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
            pa.tableVisual.SetHighlight(false);
    }

    // Repère les transports amis avec de la place, dans la même zone que cette créature — un
    // Transport ne peut pas lui-même être embarqué (pas de transports imbriqués). Voir Board().
    private void HighlightBoardableTransports()
    {
        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null || creatureLogic.CanTransport)
        {
            //Debug.Log($"[Transport] HighlightBoardableTransports — skip (creatureLogic={(creatureLogic == null ? "null" : creatureLogic.DisplayName)}, CanTransport={creatureLogic?.CanTransport})");
            return;
        }

        ZoneLogic myZone = creatureLogic.Zone;
        if (myZone == null)
        {
            //Debug.Log($"[Transport] HighlightBoardableTransports — skip, {creatureLogic.DisplayName}(ID:{creatureLogic.UniqueCreatureID}) has no Zone");
            return;
        }

        foreach (CreatureLogic other in playerOwner.playedCards.Creatures)
        {
            if (other == creatureLogic) continue;
            if (!other.CanTransport) continue;
            if (other.Zone != myZone) continue;
            if (other.BoardedCreatureIDs.Count + other.LocalPendingBoardCount >= other.TransportCapacity)
            {
                //Debug.Log($"[Transport] HighlightBoardableTransports — {other.DisplayName}(ID:{other.UniqueCreatureID}) full ({other.BoardedCreatureIDs.Count} boarded + {other.LocalPendingBoardCount} pending / {other.TransportCapacity})");
                continue;
            }

            GameObject go = IDHolder.GetGameObjectWithID(other.UniqueCreatureID);
            OneCreatureManager ocm = go != null ? go.GetComponent<OneCreatureManager>() : null;
            if (ocm == null) continue;

            _candidateTransports.Add(ocm);
            ocm.SetTransportHighlight(true);
        }
        //Debug.Log($"[Transport] HighlightBoardableTransports — {creatureLogic.DisplayName}(ID:{creatureLogic.UniqueCreatureID}) found {_candidateTransports.Count} candidate(s): [{string.Join(", ", _candidateTransports.ConvertAll(o => o.cardAsset != null ? o.cardAsset.name : "?"))}]");
    }

    private void ClearTransportHighlights()
    {
        foreach (OneCreatureManager ocm in _candidateTransports)
            if (ocm != null) ocm.SetTransportHighlight(false);
        _candidateTransports.Clear();
        _hoveredTransport = null;
    }

    // Survole les transports candidats chaque frame pendant le drag (bounds écran, voir
    // OneCreatureManager.IsScreenPointOver) plutôt qu'un raycast physique classique : la poignée de
    // drag (voir Draggable) suit la souris et masquerait tout ce qui se trouve en-dessous.
    private void UpdateHoveredTransport()
    {
        OneCreatureManager newHover = null;
        foreach (OneCreatureManager candidate in _candidateTransports)
        {
            if (candidate != null && candidate.IsScreenPointOver(Input.mousePosition))
            {
                newHover = candidate;
                break;
            }
        }

        if (newHover == _hoveredTransport)
            return;

        //Debug.Log($"[Transport] UpdateHoveredTransport — {(_hoveredTransport != null ? _hoveredTransport.cardAsset?.name : "none")} -> {(newHover != null ? newHover.cardAsset?.name : "none")}");

        if (_hoveredTransport != null)
            _hoveredTransport.SetTransportHighlight(true);
        _hoveredTransport = newHover;
        if (_hoveredTransport != null)
            _hoveredTransport.SetTransportHighlight(true, targeted: true);
    }

    // Position gauche-à-droite (0 = le plus à gauche), ghost-free, de creatureLogic dans sa propre
    // rangée mêlée/distance au sein de area — voir TableVisual.ToNetworkTablePos. N'a de sens que
    // relativement aux autres passagers d'un même embarquement groupé (voir MultiSelectionManager.
    // ConfirmGroupMove) ; pour un drag simple la valeur n'est jamais comparée à rien.
    private static int BoardOrderPosition(CreatureLogic creatureLogic, PlayerArea area)
    {
        bool isMelee = creatureLogic.IsMelee;
        GameObject creatureGO = IDHolder.GetGameObjectWithID(creatureLogic.UniqueCreatureID);
        List<GameObject> row = isMelee ? area.tableVisual.MeleeCreaturesOnTable : area.tableVisual.RangedCreaturesOnTable;
        int rawIndex = creatureGO != null ? row.IndexOf(creatureGO) : -1;
        if (rawIndex < 0) rawIndex = row.Count;
        return area.tableVisual.ToNetworkTablePos(isMelee, rawIndex);
    }

    // Embarque cette créature à bord de transportManager — même déroulé qu'un déplacement en attente
    // (Move) : réseau/solo différé bufferisent jusqu'à la résolution, immédiat résout tout de suite.
    // Contrairement à Move, aucun ghost n'est créé dans une zone cible (voir CreatureMoveVisual.Board) :
    // la créature reste visible, assombrie, en bout de sa propre rangée jusqu'à résolution.
    private bool Board(OneCreatureManager transportManager, bool silent = false)
    {
        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null)
        {
            //Debug.Log("[Transport] Board — abort, no CreatureLogic for dragged creature");
            return false;
        }

        // Pas de transports imbriqués. Le drag simple ne propose jamais un transport comme cible pour
        // une créature qui CanTransport elle-même (voir HighlightBoardableTransports, qui sort tôt
        // dans ce cas) — mais MultiSelectionManager.ConfirmGroupMove appelle TryGroupBoardTo pour
        // CHAQUE unité du multi-select sans ce filtre. Sans ce garde-fou, un transport inclus dans la
        // sélection s'embarquerait lui-même (ou un autre transport) dès que le groupe cible un transport.
        if (creatureLogic.CanTransport)
        {
            //Debug.Log($"[Transport] Board — abort, {creatureLogic.DisplayName}(ID:{creatureLogic.UniqueCreatureID}) is itself a Transport (no nested transports)");
            return false;
        }

        IDHolder transportIdHolder = transportManager.GetComponent<IDHolder>();
        if (transportIdHolder == null)
        {
            //Debug.Log("[Transport] Board — abort, transport has no IDHolder");
            return false;
        }
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(transportIdHolder.UniqueID, out CreatureLogic transportLogic))
        {
            //Debug.Log($"[Transport] Board — abort, transport ID:{transportIdHolder.UniqueID} not found in CreaturesCreatedThisGame");
            return false;
        }
        if (!transportLogic.CanTransport)
        {
            //Debug.Log($"[Transport] Board — abort, {transportLogic.DisplayName}(ID:{transportLogic.UniqueCreatureID}) is not a Transport (capacity={transportLogic.TransportCapacity})");
            return false;
        }
        if (transportLogic.BoardedCreatureIDs.Count + transportLogic.LocalPendingBoardCount >= transportLogic.TransportCapacity)
        {
            //Debug.Log($"[Transport] Board — abort, {transportLogic.DisplayName}(ID:{transportLogic.UniqueCreatureID}) full ({transportLogic.BoardedCreatureIDs.Count} boarded + {transportLogic.LocalPendingBoardCount} pending / {transportLogic.TransportCapacity})");
            if (!silent)
                new ShowMessageCommand("That transport is full.", 1f).AddToQueue();
            return false;
        }

        //Debug.Log($"[Transport] Board — {creatureLogic.DisplayName}(ID:{creatureLogic.UniqueCreatureID}) -> {transportLogic.DisplayName}(ID:{transportLogic.UniqueCreatureID}) | NetworkSession={NetworkSessionData.IsNetworkSession} DeferredSolo={GlobalSettings.Instance?.UseDeferredMovesInSolo}");

        // Capturée avant toute mutation (CancelPendingMove ne touche pas l'ordre réel de la rangée
        // d'origine, seul son affichage "en bout de rangée" — voir MarkPendingMoveAtRowEnd) : position
        // gauche-à-droite (ghost-free) de ce passager dans sa propre rangée mêlée/distance, utilisée à
        // la résolution pour trier l'ordre d'embarquement (mêlée avant distance, gauche avant droite)
        // au lieu de l'ordre chronologique des drags — voir FlushSoloBoardBuffer / FlushBuffer.
        int boardOrderPos = BoardOrderPosition(creatureLogic, originArea);

        CancelPendingMove();

        if (NetworkSessionData.IsNetworkSession)
        {
            //Debug.Log($"[Transport] Board — sending BoardCreatureServerRpc (passenger={idHolder.UniqueID}, transport={transportIdHolder.UniqueID}, playerIndex={playerOwner.playerIndex}, boardOrderPos={boardOrderPos})");
            GameNetworkManager.Instance.BoardCreatureServerRpc(idHolder.UniqueID, transportIdHolder.UniqueID, playerOwner.playerIndex, boardOrderPos);
            manager.ShowPendingMoveArrow(transportManager.CenterPointPosition);
            manager.PendingBoardTarget = transportManager;
            manager.HasPendingBoard = true;
            manager.SetPending(true, isPendingMove: true);
            // Disparait tout de suite de sa rangée d'origine, comme à la résolution réelle (voir
            // CreatureMoveVisual.Board) — seul ce client la voit disparaître ; les autres ne la
            // verront embarquer qu'à la résolution (BoardCreatureClientRpc), comme le reste du
            // preview d'action en attente (flèche, portrait). Restauré par CancelPendingMove. AVANT
            // MarkPendingMoveAtRowEnd (qui déclenche le relayout de la rangée) : TableVisual.PlaceRowOnSlots
            // exclut une créature HasPendingBoard du calcul de slots — poser le flag/la cacher après
            // laisserait ce relayout la compter et recentrerait le reste de la rangée sur une position
            // qu'elle va aussitôt quitter (cause du chevauchement observé au débarquement/annulation).
            manager.gameObject.SetActive(false);
            originArea.tableVisual.MarkPendingMoveAtRowEnd(manager.gameObject);
            transportLogic.AddLocalPendingBoard(idHolder.UniqueID);
            transportManager.RefreshPassengerPortraits();
            _pendingBoardTransportID = transportIdHolder.UniqueID;
        }
        else if (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo)
        {
            //Debug.Log($"[Transport] Board — queued via EnqueueSoloBoard (passenger={idHolder.UniqueID}, transport={transportIdHolder.UniqueID}, boardOrderPos={boardOrderPos})");
            TurnManager.Instance.EnqueueSoloBoard(idHolder.UniqueID, transportIdHolder.UniqueID, boardOrderPos);
            manager.ShowPendingMoveArrow(transportManager.CenterPointPosition);
            manager.PendingBoardTarget = transportManager;
            manager.HasPendingBoard = true;
            manager.SetPending(true, isPendingMove: true);
            // Voir commentaire équivalent ci-dessus (branche réseau) : l'ordre importe.
            manager.gameObject.SetActive(false);
            originArea.tableVisual.MarkPendingMoveAtRowEnd(manager.gameObject);
            transportLogic.AddLocalPendingBoard(idHolder.UniqueID);
            transportManager.RefreshPassengerPortraits();
            _pendingBoardTransportID = transportIdHolder.UniqueID;
        }
        else
        {
            //Debug.Log($"[Transport] Board — resolving immediately (no deferral)");
            creatureLogic.Board(transportIdHolder.UniqueID);
        }

        _skipSnapBack = true;
        return true;
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
            if (pa.parentZone == currentZone || currentZone.CanReach(pa.parentZone, playerOwner, creatureLogic))
                result.Add(pa);
        }
    }

    // Même idée que GetReachableAreasInto, pour les transports amis embarquables plutôt que les zones
    // atteignables — utilisé par MultiSelectionManager pour permettre l'embarquement groupé.
    public void GetBoardableTransportsInto(HashSet<OneCreatureManager> result)
    {
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
            return;

        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null || creatureLogic.CanTransport) return;

        ZoneLogic myZone = creatureLogic.Zone;
        if (myZone == null) return;

        foreach (CreatureLogic other in playerOwner.playedCards.Creatures)
        {
            if (other == creatureLogic) continue;
            if (!other.CanTransport) continue;
            if (other.Zone != myZone) continue;
            if (other.BoardedCreatureIDs.Count + other.LocalPendingBoardCount >= other.TransportCapacity) continue;

            GameObject go = IDHolder.GetGameObjectWithID(other.UniqueCreatureID);
            OneCreatureManager ocm = go != null ? go.GetComponent<OneCreatureManager>() : null;
            if (ocm != null) result.Add(ocm);
        }
    }

    // Pendant de TryGroupMoveTo pour l'embarquement groupé (voir MultiSelectionManager.ConfirmGroupMove).
    // silent: true — évite qu'un "That transport is full." individuel s'affiche par unité refusée,
    // l'appelant affiche un message agrégé une seule fois pour tout le groupe.
    public bool TryGroupBoardTo(OneCreatureManager transportManager)
    {
        CreatureLogic creatureLogic = GetCreatureLogic();
        if (creatureLogic == null) return false;

        originArea = playerOwner.GetPlayerAreaByID(creatureLogic.BaseID);
        if (originArea == null) return false;

        return Board(transportManager, silent: true);
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
