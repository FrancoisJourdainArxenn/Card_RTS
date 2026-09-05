using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Glisser-déposer + clic sur un portrait de la bande d'un transport (voir
// OneCreatureManager.RefreshPassengerPortraits) — même principe que le reorder de rangée
// (TableVisual.ReorderCreature) mais purement UI : réordonne par sibling index dans
// passengerPortraitsContainer plutôt que par slot 3D. Représente soit un passager, soit le
// transport lui-même (voir CreatureLogic.ManifestOrder) — le clic ne fait rien sur ce dernier.
[RequireComponent(typeof(RectTransform))]
public class PassengerPortraitDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    // Vrai tant qu'un drag de portrait est en cours — même convention que Draggable.DraggingThis /
    // MultiSelectionManager.IsConfirmingGroupMove : permet à MultiSelectionManager.Update() de ne pas
    // lancer un cadre de sélection rectangle en parallèle sur le plateau 3D (Input.GetMouseButtonDown
    // y est lu en brut, sans notion d'UI). Volontairement plus étroit qu'un simple
    // EventSystem.IsPointerOverGameObject() : d'autres éléments UI du plateau ont un Raycast Target
    // actif ailleurs sans que ça doive couper le multi-select pour autant.
    public static bool IsDraggingAny { get; private set; }

    private OneCreatureManager transportManager;
    public int RepresentedCreatureID { get; private set; }

    private RectTransform rt;
    private CanvasGroup canvasGroup;
    private LayoutElement layoutElement;
    private int originalSiblingIndex;
    private bool isDragging;

    public void Setup(OneCreatureManager transportManager, int representedCreatureID)
    {
        this.transportManager = transportManager;
        RepresentedCreatureID = representedCreatureID;
        //Debug.Log($"[Transport][Drag] Setup — {gameObject.name} representedCreatureID={representedCreatureID}, transportManager={(transportManager != null ? transportManager.name : "NULL")}");
    }

    private void Awake()
    {
        rt = GetComponent<RectTransform>();

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // La racine de la créature (Card_Board_Unit) porte un CanvasGroup avec blocksRaycasts=false
        // — vraisemblablement voulu, pour que l'UI (jusqu'ici décorative) ne vole jamais un clic
        // destiné au raycast physique 3D qui gère le drag des créatures (voir TableVisual.Update).
        // Un blocksRaycasts=false sur UN ancêtre désactive le raycast pour TOUS ses descendants, quel
        // que soit leur propre Raycast Target — ignoreParentGroups fait sortir CE portrait précis de
        // ce blocage, sans rien changer au comportement des autres éléments (Frame, Background...).
        canvasGroup.ignoreParentGroups = true;

        // ignoreLayout=true pendant le drag : sans ça, le HorizontalLayoutGroup du conteneur écrase
        // notre rt.position à chaque frame et l'icône ne suit jamais le curseur.
        layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null) layoutElement = gameObject.AddComponent<LayoutElement>();

        //Debug.Log($"[Transport][Drag] Awake — {gameObject.name} initialized (rt={(rt != null ? "ok" : "NULL")}, canvasGroup={(canvasGroup != null ? "ok" : "NULL")}, layoutElement={(layoutElement != null ? "ok" : "NULL")})");
    }

    private bool IsTransportSelf
    {
        get
        {
            if (transportManager == null) return false;
            IDHolder idHolder = transportManager.GetComponent<IDHolder>();
            return idHolder != null && RepresentedCreatureID == idHolder.UniqueID;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        //Debug.Log($"[Transport][Drag] OnPointerClick — {gameObject.name} (representedCreatureID={RepresentedCreatureID}, isDragging={isDragging}, transportManager={(transportManager != null ? transportManager.name : "NULL")}, IsTransportSelf={IsTransportSelf})");
        if (isDragging || transportManager == null || IsTransportSelf) return;
        //Debug.Log($"[Transport][Drag] OnPointerClick — calling RequestDisembarkPassenger({RepresentedCreatureID}) on {transportManager.name}");
        transportManager.RequestDisembarkPassenger(RepresentedCreatureID);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        //Debug.Log($"[Transport][Drag] OnBeginDrag — {gameObject.name} (transportManager={(transportManager != null ? transportManager.name : "NULL")}, CanReorderNow={(transportManager != null ? transportManager.CanReorderNow.ToString() : "n/a")})");
        if (transportManager == null || !transportManager.CanReorderNow) return;

        isDragging = true;
        IsDraggingAny = true;
        originalSiblingIndex = transform.GetSiblingIndex();
        canvasGroup.blocksRaycasts = false;
        layoutElement.ignoreLayout = true;
        transform.SetAsLastSibling();
        //Debug.Log($"[Transport][Drag] OnBeginDrag — started, originalSiblingIndex={originalSiblingIndex}");
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        RectTransform parentRT = transform.parent as RectTransform;
        if (parentRT != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parentRT, eventData.position, eventData.pressEventCamera, out Vector3 worldPoint))
        {
            rt.position = worldPoint;
        }

        // Trouve le premier frère (de gauche à droite) que le curseur a dépassé, et prend sa place.
        Transform parent = transform.parent;
        int targetIndex = parent.childCount - 1;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform sibling = parent.GetChild(i);
            if (sibling == transform) continue;
            if (rt.position.x < sibling.position.x)
            {
                targetIndex = sibling.GetSiblingIndex();
                break;
            }
        }
        if (targetIndex != transform.GetSiblingIndex())
        {
            //Debug.Log($"[Transport][Drag] OnDrag — {gameObject.name} moving to sibling index {targetIndex} (rt.position.x={rt.position.x})");
            transform.SetSiblingIndex(targetIndex);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        //Debug.Log($"[Transport][Drag] OnEndDrag — {gameObject.name} (isDragging={isDragging})");
        if (!isDragging) return;
        isDragging = false;
        IsDraggingAny = false;

        canvasGroup.blocksRaycasts = true;
        layoutElement.ignoreLayout = false;

        if (transportManager == null || !transportManager.CanReorderNow)
        {
            //Debug.Log($"[Transport][Drag] OnEndDrag — reverting to originalSiblingIndex={originalSiblingIndex} (transportManager={(transportManager != null ? transportManager.name : "NULL")}, CanReorderNow={(transportManager != null ? transportManager.CanReorderNow.ToString() : "n/a")})");
            transform.SetSiblingIndex(originalSiblingIndex);
            return;
        }

        //Debug.Log($"[Transport][Drag] OnEndDrag — committing new order via CommitManifestOrderFromUI()");
        transportManager.CommitManifestOrderFromUI();
    }
}
