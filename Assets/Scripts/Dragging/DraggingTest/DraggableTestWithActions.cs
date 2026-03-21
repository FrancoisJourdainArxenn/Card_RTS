using UnityEngine;
using System.Collections;

public class DraggableTestWithActions : MonoBehaviour {

    public bool UsePointerDisplacement = true;
    // PRIVATE FIELDS
    // a reference to a DraggingActionsTest script
    private DraggingActionsTest da;

    // a flag to know if we are currently dragging this GameObject
    private bool dragging = false;

    // distance from the center of this Game Object to the point where we clicked to start dragging 
    private Vector3 pointerDisplacement = Vector3.zero;

    // MONOBEHAVIOUR METHODS
    void Awake()
    {
        da = GetComponent<DraggingActionsTest>();
    }

    void OnMouseDown()
    {
        if (da.CanDrag)
        {
            dragging = true;
            HoverPreview.PreviewsAllowed = false;       // NEW LINE
            da.OnStartDrag();
            Vector3 mousePos = MouseInWorldCoords();
            if (UsePointerDisplacement)
            {
                // store displacement only on XZ plane
                pointerDisplacement = new Vector3(
                    mousePos.x - transform.position.x,
                    0f,
                    mousePos.z - transform.position.z
                );
            }
            else
            {
                pointerDisplacement = Vector3.zero;
            }
        }
    }

    // Update is called once per frame
    void Update ()
    {
        if (dragging)
        { 
            Vector3 mousePos = MouseInWorldCoords();
            da.OnDraggingInUpdate();
            //Debug.Log(mousePos);
            // move on XZ plane, keep current height (Y)
            transform.position = new Vector3(
                mousePos.x - pointerDisplacement.x,
                transform.position.y,
                mousePos.z - pointerDisplacement.z
            );   
        }
    }

    void OnMouseUp()
    {
        if (dragging)
        {
            dragging = false;
            HoverPreview.PreviewsAllowed = true;   // NEW LINE
            da.OnEndDrag();
        }
    }   

    // returns mouse position in World coordinates for our GameObject to follow. 
    private Vector3 MouseInWorldCoords()
    {
        Plane dragPlane = new Plane(Vector3.up, transform.position);
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        float enter;
        if (dragPlane.Raycast(ray, out enter))
        {
            return ray.GetPoint(enter);
        }
        // fallback : si jamais le raycast rate (très rare)
        return transform.position;
    }
}
