using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            return base.CanDrag && manager.CanBeDraggedNow && !_isReturning && !_isPlayed
                && !OnPlayTargetingSession.IsActive;
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
        if (Input.GetMouseButtonDown(1))
        {
            Draggable.CancelCurrentDrag();
            return;
        }
        UpdateInsertPreview();
    }

    // Annulation programmatique (clic droit / Echap) pendant un drag en cours — voir
    // Draggable.CancelCurrentDrag. Reproduit la branche "échec" de OnEndDrag : la carte revient en main.
    public override void OnDragCancelled()
    {
        ClearInsertPreview();
        transform.localScale = _originalScale;
        ResetAreaHighlights();
        DragFailed();
    }

    public override void OnEndDrag()
    {
        ClearInsertPreview();
        transform.localScale = _originalScale;
        ResetAreaHighlights();

        CardHoldSlotVisual targetSlot = CardHoldSlotVisual.SlotUnderCursor;
        if (targetSlot != null && targetSlot.TryHoldCard(gameObject, playerOwner))
            return;

        // 1) Check if we are holding a card over the table
        bool dragOk = DragSuccessful();
        if (dragOk)
        {
            PlayerArea selectedPArea = playerOwner.SelectedPArea();
            bool isMelee = manager.cardAsset.melee;
            // Index visuel brut → index logique (ghosts exclus) : c'est ce dernier qui doit transiter
            // par le réseau / le buffer différé pour garder le même sens sur tous les clients
            // (voir TableVisual.ToNetworkTablePos).
            int visualTablePos = selectedPArea.tableVisual.TablePosForNewCreature(isMelee);
            int tablePos = selectedPArea.tableVisual.ToNetworkTablePos(isMelee, visualTablePos);

            int cardID = GetComponent<IDHolder>().UniqueID;

            // Si un effet OnPlay de cette carte requiert une sélection du joueur (Require Player
            // Selection), on choisit la/les cible(s) localement AVANT de committer quoi que ce soit
            // (dépense de ressource, appel réseau) — rien n'est committé tant que ce n'est pas confirmé.
            List<PendingEffectSelection> requiredSelections = OnPlayTargetingSession.CollectRequiredSelections(
                manager.cardAsset, playerOwner, selectedPArea.parentZone.Logic);

            if (requiredSelections.Count == 0)
            {
                _isPlayed = true;
                ReleaseHoldSlotIfAny();
                CommitPlay(cardID, tablePos, selectedPArea, null);
                GetComponent<Draggable>().enabled = false;
            }
            else
            {
                // Confort visuel : la carte laisse place à un placeholder de créature posé dans la
                // zone (comme une pose normale), qui sert d'ancre réelle au popup et à la flèche de
                // ciblage classiques (EffectTargetingArrow/CardPreviewUI attendent une vraie entité).
                // ID purement local/visuel : rien n'est committé tant que ce n'est pas confirmé,
                // annuler ne fait que retirer ce ghost.
                int ghostID = IDFactory.GetLocalOnlyID();
                selectedPArea.tableVisual.AddCreatureAtIndex(
                    manager.cardAsset, ghostID, visualTablePos, selectedPArea.baseID, completeCommand: false);

                GameObject ghostGO = IDHolder.GetGameObjectWithID(ghostID);
                if (ghostGO != null && ghostGO.TryGetComponent(out OneCreatureManager ghostOcm))
                    ghostOcm.SetPending(true);

                foreach (PendingEffectSelection sel in requiredSelections)
                    sel.SourceEntityID = ghostID;

                gameObject.SetActive(false);

                OnPlayTargetingSession.Begin(
                    requiredSelections,
                    onConfirmed: selections =>
                    {
                        _isPlayed = true;
                        ReleaseHoldSlotIfAny();
                        selectedPArea.tableVisual.RemovePendingMoveGhost(ghostGO);
                        CommitPlay(cardID, tablePos, selectedPArea, selections);
                        GetComponent<Draggable>().enabled = false;
                    },
                    onCancelled: () =>
                    {
                        selectedPArea.tableVisual.RemovePendingMoveGhost(ghostGO);
                        gameObject.SetActive(true);
                        DragFailed();
                    });
            }
        }
        else
        {
            DragFailed();
        }
    }

    private void ReleaseHoldSlotIfAny()
    {
        if (whereIsCard.HoldSlot != null)
        {
            whereIsCard.HoldSlot.ReleaseCard(gameObject);
            whereIsCard.HoldSlot = null;
        }
    }

    private void CommitPlay(int cardID, int tablePos, PlayerArea selectedPArea, List<PendingEffectSelection> selections)
    {
        if (NetworkSessionData.IsNetworkSession)
        {
            int playerIndex = System.Array.IndexOf(Player.Players, playerOwner);
            int[] effectIndexes     = selections?.Select(s => s.EffectIndexInCard).ToArray() ?? new int[0];
            int[] selectedTargetIDs = selections?.Select(s => s.SelectedTarget?.ID ?? -1).ToArray() ?? new int[0];
            GameNetworkManager.Instance.PlayCreatureServerRpc(
                cardID, tablePos, selectedPArea.baseID, playerIndex, effectIndexes, selectedTargetIDs);
        }
        else
        {
            playerOwner.PlayACreatureFromHand(cardID, tablePos, selectedPArea, selections);
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
        if (!CanAffordThisCard())
        {
            new ShowMessageCommand("Not enough resources", 2f).AddToQueue();
            return false;
        }
        return true;
    }

    // Le drag est maintenant autorisé même sans assez de ressource (voir OneCardManager.CanBeDraggedNow) —
    // pour pouvoir déposer la carte dans un CardHoldSlotVisual malgré tout. On revalide donc le coût
    // ici, au moment de la pose effective sur la table, avec le CardLogic (coût potentiellement
    // modifié par un effet) plutôt que le CardAsset de base.
    private bool CanAffordThisCard()
    {
        IDHolder id = GetComponent<IDHolder>();
        return id != null
            && CardLogic.CardsCreatedThisGame.TryGetValue(id.UniqueID, out CardLogic cl)
            && cl.MainCost <= playerOwner.MainRessourceAvailable;
    }

    private void DragFailed()
    {
        StartCoroutine(ReturnToHand());
    }

    private IEnumerator ReturnToHand()
    {
        _isReturning = true;
        if (whereIsCard.HoldSlot != null)
            whereIsCard.SetHoldSlotSortingOrder();
        else
            whereIsCard.SetHandSortingOrder();
        whereIsCard.VisualState = tempState;
        Vector3 oldCardPos = whereIsCard.HoldSlot != null
            ? whereIsCard.HoldSlot.cardAnchor.localPosition
            : playerOwner.handVisual.slots.Children[savedHandSlot].transform.localPosition;
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
