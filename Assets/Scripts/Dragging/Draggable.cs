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

    // Nettoyage d'état partagé entre un relâchement normal (OnMouseUp) et une fin de drag
    // déclenchée par du code (Echap, hand-off vers une session de ciblage) : previews réactivés,
    // référence statique libérée. Retourne l'instance concernée (ou null si rien n'était en cours).
    private static Draggable ResetDragState()
    {
        if (_draggingThis == null)
            return null;

        Draggable current = _draggingThis;
        current.dragging = false;
        HoverPreview.PreviewsAllowed = true;
        _draggingThis = null;
        return current;
    }

    // Annule programmatiquement le drag en cours (ex: touche Echap) sans attendre un OnMouseUp —
    // le bouton de la souris peut rester physiquement enfoncé. Délègue à OnDragCancelled() plutôt
    // qu'à OnEndDrag(), pour laisser chaque DraggingActions distinguer "annulé" de "relâché
    // normalement". No-op si rien n'est en cours de drag.
    public static void CancelCurrentDrag()
    {
        ResetDragState()?.da.OnDragCancelled();
    }

    // Termine le drag en cours sans appeler OnEndDrag() ni OnDragCancelled() — utilisé quand un
    // DraggingActions prend lui-même le relais en plein OnStartDrag() (ex: DragSpellOnTarget qui
    // bascule immédiatement sur OnPlayTargetingSession) : le drag "souris" doit s'arrêter net,
    // sans qu'aucun des deux callbacks de fin ne s'exécute. No-op si rien n'est en cours de drag.
    public static void EndDragSilently()
    {
        ResetDragState();
    }

    // Empêche le TOUT PROCHAIN OnMouseDown de démarrer un drag, sans toucher à celui déjà en cours
    // (le cas échéant). Sert quand un clic vient d'être consommé par un ciblage de joueur (voir
    // OneCreatureManager.OnCreatureClicked) : sur le même GameObject, HoverPreview.OnMouseDown()
    // s'exécute avant Draggable.OnMouseDown() (ordre des composants dans le prefab), donc résoudre
    // le ciblage AVANT que CanDrag ne soit évalué peut faire retomber IsPlayerTargetingComplete à
    // true dans la même frame (ex: dernière cible d'une file) — CanDrag ne suffit alors plus à
    // bloquer le drag. Ce flag comble cette fenêtre.
    // Verrouillé sur le numéro de frame où il est posé : Unity n'envoie OnMouseDown qu'à UN SEUL
    // GameObject par frame (celui sous le raycast), donc si personne ne le consomme cette frame-là
    // (ex: variant de prefab sans Draggable), il expire tout seul au lieu de fuiter vers le
    // prochain vrai clic de drag, potentiellement ailleurs et bien plus tard.
    private static bool _suppressNextMouseDown = false;
    private static int _suppressFrame = -1;
    public static void SuppressNextMouseDown()
    {
        _suppressNextMouseDown = true;
        _suppressFrame = Time.frameCount;
    }

    // MONOBEHAVIOUR METHODS
    void Awake()
    {
        da = GetComponent<DraggingActions>();
    }

    void OnMouseDown()
    {
        bool suppressed = _suppressNextMouseDown && _suppressFrame == Time.frameCount;
        _suppressNextMouseDown = false;
        if (suppressed)
            return;

        if (da!=null && da.CanDrag)
        {
            Debug.Log("mousedown");
            dragging = true;
            // when we are dragging something, all previews should be off
            HoverPreview.PreviewsAllowed = false;
            _draggingThis = this;
            da.OnStartDrag();
            //zDisplacement = -Camera.main.transform.position.z + transform.position.z;
            pointerDisplacement = Vector3.zero;

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
            ResetDragState();
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
