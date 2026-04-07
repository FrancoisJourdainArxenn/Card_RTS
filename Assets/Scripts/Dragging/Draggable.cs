using UnityEngine;
using System.Collections;
using DG.Tweening;

/// <summary>
/// This class enables Drag and Drop Behaviour for the game object it is attached to. 
/// It uses other script - DraggingActions to determine whether we can drag this game object now or not and 
/// whether the drop was successful or not.
/// </summary>

public class Draggable : MonoBehaviour {

    // PRIVATE FIELDS

    // a flag to know if we are currently dragging this GameObject
    private bool dragging = false;

    // distance from the center of this Game Object to the point where we clicked to start dragging 
    private Vector3 pointerDisplacement;

    // distance from camera to mouse on Z axis 
    private float zDisplacement;

    // reference to DraggingActions script. Dragging Actions should be attached to the same GameObject.
    private DraggingActions da;

    // STATIC property that returns the instance of Draggable that is currently being dragged
    private static Draggable _draggingThis;
    public static Draggable DraggingThis
    {
        get{ return _draggingThis;}
    }

    // MONOBEHAVIOUR METHODS
    void Awake()
    {
        da = GetComponent<DraggingActions>();
    }

    void OnMouseDown()
    {
        Debug.Log($"[Draggable] OnMouseDown on '{name}' tag='{tag}' layer='{LayerMask.LayerToName(gameObject.layer)}'");
        if (da == null)
        {
            Debug.LogError($"[Draggable] DraggingActions missing on SAME GameObject '{name}'.");
            return;
        }
        Debug.Log($"[Draggable] Found DraggingActions={da.GetType().Name}, CanDrag={da.CanDrag}");

        if (da!=null && da.CanDrag)
        {
            dragging = true;
            // when we are dragging something, all previews should be off
            HoverPreview.PreviewsAllowed = false;
            _draggingThis = this;
            da.OnStartDrag();
            //zDisplacement = -Camera.main.transform.position.z + transform.position.z;
            Vector3 mousePos = MouseInWorldCoords();
            pointerDisplacement = new Vector3(
                mousePos.x - transform.position.x,
                0f,
                mousePos.z - transform.position.z
            );
        }
    }

    // Update is called once per frame
    void Update ()
    {
        if (dragging)
        { 
            Vector3 mousePos = MouseInWorldCoords();
            //Debug.Log(mousePos);
            transform.position = new Vector3(mousePos.x - pointerDisplacement.x,transform.position.y, mousePos.z - pointerDisplacement.z);   
            da.OnDraggingInUpdate();
        }
    }
	
    void OnMouseUp()
    {
        if (dragging)
        {
            dragging = false;
            // turn all previews back on
            HoverPreview.PreviewsAllowed = true;
            _draggingThis = null;
            da.OnEndDrag();
        }
    }   

    // returns mouse position in World coordinates for our GameObject to follow. 
    private Vector3 MouseInWorldCoords()
    {
        // Plan horizontal passant par la position actuelle de l’objet
        Plane dragPlane = new Plane(Vector3.up, transform.position);
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        float enter;
        if (dragPlane.Raycast(ray, out enter))
        {
            return ray.GetPoint(enter);  // point 3D sur le plan
        }
        // fallback au cas où (très rare) : on reste où on est
        return transform.position;
    }
        
}
