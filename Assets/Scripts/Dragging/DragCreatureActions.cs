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
    [SerializeField] private BezierArrows targettingArrow;
    void Awake()
    {
        // establish all the connections
        sr = GetComponent<SpriteRenderer>();
        manager = GetComponentInParent<OneCreatureManager>();
        whereIsThisCreature = GetComponentInParent<WhereIsTheCardOrCreature>();
        targettingArrow.originOverride = transform.parent;

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
        
        /*if (turnmanager.CurrentPhase == TurnManager.TurnPhases.Battle) {
            SelectTarget();
            bool targetValid = AttackTarget();
            if (!targetValid)
                OnDragFailed();
        }*/
        if (turnmanager.CurrentPhase == TurnManager.TurnPhases.Command) {
            PlayerArea selectedPArea = playerOwner.SelectedPArea();
            bool moveValid = Move(selectedPArea);  

            if (!moveValid)
                OnDragFailed();
        }
        
        // return target and arrow to original position
        ResetDragElements();
    }
    private void SelectTarget()
    {
        target = null;
        RaycastHit[] hits;
        // TODO: raycast here anyway, store the results in
        hits = Physics.RaycastAll(origin: Camera.main.transform.position, 
            direction: (-Camera.main.transform.position + this.transform.position).normalized, 
            maxDistance: 30f);

        foreach (RaycastHit h in hits)
        {
            if ((h.transform.tag == "TopPlayer" && this.tag == "LowCreature") ||
                (h.transform.tag == "LowPlayer" && this.tag == "TopCreature"))
            {
                // go face
                IDHolder hitIdHolder = h.transform.GetComponentInParent<IDHolder>();
                if (hitIdHolder != null)
                    target = hitIdHolder.gameObject;
            }
            else if ((h.transform.tag == "TopCreature" && this.tag == "LowCreature") ||
                    (h.transform.tag == "LowCreature" && this.tag == "TopCreature"))
            {
                // hit a creature, resolve to the object that actually carries the ID
                IDHolder hitIdHolder = h.transform.GetComponentInParent<IDHolder>();
                if (hitIdHolder != null)
                    target = hitIdHolder.gameObject;
            }
            
        }
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

        ZoneView currentZone = originArea.parentZone;
        ZoneView targetZone = targetPlayerArea.parentZone;
        if (currentZone != targetZone && !currentZone.IsAdjacentTo(targetZone))
        {
            new ShowMessageCommand("Zone not in range", 1f).AddToQueue();
            return false;
        }

        IDHolder moverIdHolder = GetComponentInParent<IDHolder>();
        if (moverIdHolder == null)
        {
            Debug.Log("pas d'ID pour le mover");
            return false;
        }

        if (!CreatureLogic.CreaturesCreatedThisGame.ContainsKey(moverIdHolder.UniqueID))
        {
            Debug.Log("mover not found");
            return false;
        }
        int tablePos = targetPlayerArea.tableVisual.TablePosForNewCreature(
            Camera.main.ScreenToWorldPoint(
                new Vector3(
                    Input.mousePosition.x,
                    Input.mousePosition.y,
                    transform.position.z - Camera.main.transform.position.z
                )
            ).x
        );
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
    
    private bool AttackTarget()
    {
        if(target == null)
        {
            Debug.Log("target null");
            return false;
        }
   
        IDHolder targetIdHolder = target.GetComponent<IDHolder>();
        if (targetIdHolder == null)
            targetIdHolder = target.GetComponentInParent<IDHolder>();

        IDHolder attackerIdHolder = GetComponentInParent<IDHolder>();
        if (targetIdHolder == null || attackerIdHolder == null)
        {
            Debug.Log("ID holder de target or attacker is null");
            return false;
        }
               
        int targetID = targetIdHolder.UniqueID;
        int attackerID = attackerIdHolder.UniqueID;

        if (targetID == attackerID)
        {
            Debug.Log("target or attacker ID null");
            return false;
        }

        if (!CreatureLogic.CreaturesCreatedThisGame.ContainsKey(attackerID))
        {
            Debug.Log("Attacker not found");
            return false;
        }

        if (targetID == GlobalSettings.Instance.LowPlayer.PlayerID || targetID == GlobalSettings.Instance.TopPlayer.PlayerID)
        {
            Player targetPlayer = (targetID == GlobalSettings.Instance.LowPlayer.PlayerID)
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
            if (targetPlayer.MainPArea.parentZone != originArea.parentZone)
            {
                new ShowMessageCommand("Base not in range", 1f).AddToQueue();
                return false;
            }
            Debug.Log("Attacking face" + target);
            if (NetworkSessionData.IsNetworkSession)
                GameNetworkManager.Instance.GoFaceServerRpc(attackerID);
            else
                CreatureLogic.CreaturesCreatedThisGame[attackerID].GoFace();
            return true;
        }
        
        if (BaseLogic.BasesCreatedThisGame.ContainsKey(targetID) &&
            BaseLogic.BasesCreatedThisGame[targetID] != null)
        {
            BaseLogic bl = BaseLogic.BasesCreatedThisGame[targetID];
            if (bl.neutralBaseController.zone != originArea.parentZone)
            {
                new ShowMessageCommand("Base not in range", 1f).AddToQueue();
                return false;
            }
            if (NetworkSessionData.IsNetworkSession)
                GameNetworkManager.Instance.AttackBaseServerRpc(attackerID, targetID);
            else
                CreatureLogic.CreaturesCreatedThisGame[attackerID].AttackBaseWithID(targetID);
            Debug.Log("Attacking base " + target);
            return true;  

        }
        if (CreatureLogic.CreaturesCreatedThisGame.ContainsKey(targetID) &&
            CreatureLogic.CreaturesCreatedThisGame[targetID] != null)
        {
            // if targeted creature is still alive, attack creature
            CreatureLogic cl = CreatureLogic.CreaturesCreatedThisGame[targetID];
            if (cl.Targetable)
            {
                PlayerArea targetArea = cl.owner.GetPlayerAreaByID(cl.BaseID);
                if (targetArea.parentZone != originArea.parentZone)
                {
                    new ShowMessageCommand("Unit not in range", 1f).AddToQueue();
                    return false;
                }
                if (NetworkSessionData.IsNetworkSession)
                    GameNetworkManager.Instance.AttackCreatureServerRpc(attackerID, targetID);
                else
                    CreatureLogic.CreaturesCreatedThisGame[attackerID].AttackCreatureWithID(targetID);
                Debug.Log("Attacking Creature " + target);
                return true;                
            }
            new ShowMessageCommand("You can't target this unit", 2f).AddToQueue();
            return false;
        }            

        Debug.Log("Unknown Error");
        return false;
    }

    private void ResetDragElements()
    {
        // ResetColorizeUnits();
        ResetAreaHighlights();

        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
        sr.enabled = false;
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
        if (TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
            return;
    
        if (originArea == null || originArea.parentZone == null)
            return;

        ZoneView currentZone = originArea.parentZone;
        foreach (PlayerArea pa in FindObjectsByType<PlayerArea>(FindObjectsSortMode.None))
        {
            if (pa == originArea) continue;
            if (!System.Array.Exists(playerOwner.PAreas, a => a == pa)) continue;
            if (pa.parentZone == currentZone || currentZone.IsAdjacentTo(pa.parentZone))
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
