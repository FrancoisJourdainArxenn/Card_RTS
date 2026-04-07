using UnityEngine;
using System.Collections;

public class DragCreatureAttack : DraggingActions {

    // reference to the sprite with a round "Target" graphic
    private SpriteRenderer sr;
    // LineRenderer that is attached to a child game object to draw the arrow
    private LineRenderer lr;
    // reference to WhereIsTheCardOrCreature to track this object`s state in the game
    private WhereIsTheCardOrCreature whereIsThisCreature;
    // the pointy end of the arrow, should be called "Triangle" in the Hierarchy
    private Transform triangle;
    // SpriteRenderer of triangle. We need this to disable the pointy end if the target is too close.
    private SpriteRenderer triangleSR;
    // when we stop dragging, the gameObject that we were targeting will be stored in this variable.
    private GameObject target;
    // Reference to creature manager, attached to the parent game object
    private OneCreatureManager manager;

    void Awake()
    {
        // establish all the connections
        sr = GetComponent<SpriteRenderer>();
        lr = GetComponentInChildren<LineRenderer>();
        lr.sortingLayerName = "AboveEverything";
        triangle = transform.Find("Triangle");
        triangleSR = triangle.GetComponent<SpriteRenderer>();

        manager = GetComponentInParent<OneCreatureManager>();
        whereIsThisCreature = GetComponentInParent<WhereIsTheCardOrCreature>();
    }

    public override bool CanDrag
    {
        get
        {   
            return base.CanDrag && (manager.CanAttackNow || manager.CanMoveNow);
        }
    }

    public override void OnStartDrag()
    {
        whereIsThisCreature.VisualState = VisualStates.Dragging;
        // enable target graphic
        sr.enabled = true;
        // enable line renderer to start drawing the line.
        lr.enabled = true;
    }

    public override void OnDraggingInUpdate()
    {
        Vector3 notNormalized = transform.position - transform.parent.position;
        Vector3 direction = notNormalized.normalized;
        float distanceToTarget = (direction*2.3f).magnitude;
        if (notNormalized.magnitude > distanceToTarget)
        {
            // draw a line between the creature and the target
            lr.SetPositions(new Vector3[]{ transform.parent.position, transform.position - direction*2.3f });
            lr.enabled = true;

            // position the end of the arrow between near the target.
            triangleSR.enabled = true;
            triangleSR.transform.position = transform.position - 1.5f*direction;

            // proper rotation of arrow end
            // Compute angle in screen space, then apply it on LOCAL Z
            // so inspector values stay in the expected 0/0/X form.
            Vector3 screenPos = Camera.main.WorldToScreenPoint(triangleSR.transform.position);
            Vector3 screenParent = Camera.main.WorldToScreenPoint(transform.parent.position);
            Vector2 dir = (screenPos - screenParent);
            float rot_z = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            triangleSR.transform.localRotation = Quaternion.Euler(0f, 0f, rot_z - 90f);
        }
        else
        {
            // if the target is not far enough from creature, do not show the arrow
            lr.enabled = false;
            triangleSR.enabled = false;
        }
            
    }

    public override void OnEndDrag()
    {
        TurnManager turnmanager = TurnManager.Instance;
        
        if (turnmanager.CurrentPhase == TurnManager.TurnPhases.Battle) {
            SelectTarget();
            bool targetValid = AttackTarget();
            if (!targetValid)
                OnDragFailed();
        }
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
    
        if (targetPlayerArea == playerOwner.SelectedPArea())
        {
            Debug.Log("target player area is the same as the player area");
            return false;
        }

        manager.Move(targetPlayerArea);
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
            Debug.Log("Attacker ID not found");
            return false;
        }

        if (targetID == GlobalSettings.Instance.LowPlayer.PlayerID || targetID == GlobalSettings.Instance.TopPlayer.PlayerID)
        {
            // attack character
            Debug.Log("Attacking " + target);
            Debug.Log("target: " + targetID);
            CreatureLogic.CreaturesCreatedThisGame[attackerID].GoFace();
            return true;
        }
        
        if (BuildingLogic.BuildingsCreatedThisGame.ContainsKey(targetID) &&
            BuildingLogic.BuildingsCreatedThisGame[targetID] != null)
        {
            CreatureLogic.CreaturesCreatedThisGame[attackerID].AttackBuildingWithID(targetID);
            Debug.Log("Attacking building " + target);
            return true;  

        }
        if (CreatureLogic.CreaturesCreatedThisGame.ContainsKey(targetID) &&
            CreatureLogic.CreaturesCreatedThisGame[targetID] != null)
        {
            // if targeted creature is still alive, attack creature
            CreatureLogic.CreaturesCreatedThisGame[attackerID].AttackCreatureWithID(targetID);
            Debug.Log("Attacking " + target);
            return true;
        }            

        Debug.Log("Unknown Error");
        return false;
    }

    private void ResetDragElements()
    {
        transform.localPosition = Vector3.zero;
        sr.enabled = false;
        lr.enabled = false;
        triangleSR.enabled = false;

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

    // NOT USED IN THIS SCRIPT
    protected override bool DragSuccessful()
    {
        return true;
    }
}
