using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DragCreatureActions : DraggingActions {

    // reference to the sprite with a round "Target" graphic
    private SpriteRenderer sr;
    // reference to WhereIsTheCardOrCreature to track this object`s state in the game
    private WhereIsTheCardOrCreature whereIsThisCreature;
    private GameObject target;
    // Reference to creature manager, attached to the parent game object
    private OneCreatureManager manager;
    private Vector3 originalLocalPosition;

    
    [SerializeField] private CurvedArrow targettingArrow;
    void Awake()
    {
        // establish all the connections
        sr = GetComponent<SpriteRenderer>();
        manager = GetComponentInParent<OneCreatureManager>();
        whereIsThisCreature = GetComponentInParent<WhereIsTheCardOrCreature>();
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
        manager.ClearPendingMoveArrow();

        originArea = playerOwner.SelectedPArea();
        whereIsThisCreature.VisualState = VisualStates.Dragging;
        // enable target graphic
        sr.enabled = true;
        targettingArrow.Show();
        HighlightReachableAreas();
        //ColorizeUnits();

    }

    public override void OnDraggingInUpdate()
    {
        
    }
    public override void OnEndDrag()
    {
        TurnManager turnmanager = TurnManager.Instance;
        
        if (turnmanager.CurrentPhase == TurnManager.TurnPhases.Command) {
            PlayerArea selectedPArea = playerOwner.SelectedPArea();
            bool moveValid = Move(selectedPArea);  

            if (!moveValid)
                OnDragFailed();
        }
        
        // return target and arrow to original position
        ResetDragElements();
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

        transform.SetLocalPositionAndRotation(originalLocalPosition, Quaternion.Euler(90f, 0f, 0f));        sr.enabled = false;
        targettingArrow.Hide();


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
