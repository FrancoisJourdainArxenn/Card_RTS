using UnityEngine;
using System.Collections;

public class DragCreatureMove : DraggingActions {

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
        Debug.Log($"[DragCreatureMove] Awake on '{name}' manager={(manager!=null)} whereIs={(whereIsThisCreature!=null)} lr={(lr!=null)} triangle={(triangle!=null)}");

    }

    public override bool CanDrag
    {
        get
        {   
            bool baseCanDrag = base.CanDrag;
            bool canMoveNow = manager != null && manager.CanMoveNow;
            bool result = baseCanDrag && canMoveNow;
            Debug.Log($"[DragCreatureMove] CanDrag obj='{name}' baseCanDrag={baseCanDrag} managerNull={(manager==null)} canMoveNow={canMoveNow} => {result}");
            return result;
        }
    }

    public override void OnStartDrag()
    {
        Debug.Log($"[DragCreatureMove] OnStartDrag called on '{name}'");
        whereIsThisCreature.VisualState = VisualStates.Dragging;
        // enable target graphic
        sr.enabled = true;
        // enable line renderer to start drawing the line.
        lr.enabled = true;

        Debug.Log("DragCreatureMove OnStartDrag");
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
        PlayerArea selectedPArea = playerOwner.SelectedPArea();
        bool moveValid = Move(selectedPArea);  

        if (!moveValid)
        {
            // not a valid move, return
            if (tag.Contains("Low"))
                whereIsThisCreature.VisualState = VisualStates.LowTable;
            else
                whereIsThisCreature.VisualState = VisualStates.TopTable;
            whereIsThisCreature.SetTableSortingOrder();
        }

        // return target and arrow to original position
        transform.localPosition = Vector3.zero;
        sr.enabled = false;
        lr.enabled = false;
        triangleSR.enabled = false;

    }

    private bool Move(PlayerArea targetPlayerArea)
    {
        if(targetPlayerArea == null)
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

    // NOT USED IN THIS SCRIPT
    protected override bool DragSuccessful()
    {
        return true;
    }
}
