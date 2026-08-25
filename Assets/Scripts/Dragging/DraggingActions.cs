using UnityEngine;
using System.Collections;

public abstract class DraggingActions : MonoBehaviour {

    public abstract void OnStartDrag();

    public abstract void OnEndDrag();

    public abstract void OnDraggingInUpdate();

    // Appelé quand un drag en cours est annulé par du code (ex: touche Echap) plutôt que par un
    // relâchement de souris normal — voir Draggable.CancelCurrentDrag(). No-op par défaut : seuls
    // les DraggingActions qui ont besoin d'un comportement d'annulation spécifique le surchargent.
    public virtual void OnDragCancelled() { }


    public virtual bool CanDrag
    {
        get
        {            
            Player po = playerOwner;
            bool canControl = (GlobalSettings.Instance != null && po != null) 
                ? GlobalSettings.Instance.CanControlThisPlayer(po)
                : false;
            return canControl;
        }
    }

    protected virtual Player playerOwner
    {
        get{
            
            if (tag.Contains("Low"))
                return GlobalSettings.Instance.LowPlayer;
            else if (tag.Contains("Top"))
                return GlobalSettings.Instance.TopPlayer;
            else
            {
                Debug.LogError("Untagged Card or creature " + transform.parent.name);
                return null;
            }
        }
    }

    protected abstract bool DragSuccessful();
}
