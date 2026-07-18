using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.UI;

public class MultiSelectionManager : MonoBehaviour
{
    public RectTransform selectionBox;
    public Canvas parentCanvas;
    public int pixelBeforeAppartion = 5;
    [SerializeField] private HoverArrow groupMoveArrow;

    public List<OneCreatureManager> AllSelectableObjects = new List<OneCreatureManager>();
    public List <OneCreatureManager> CurrSelectedObjects = new List<OneCreatureManager>();

    bool isMouseDown, isDragging, groupMoveActive;
    Vector3 MouseStartPos;
    readonly List<PlayerArea> highlightedAreas = new List<PlayerArea>();

    Vector2 ScreenToLocal(Vector3 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            selectionBox.parent as RectTransform,
            screenPos,
            parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera,
            out Vector2 localPoint);
        return localPoint;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        isDragging = false;
        isMouseDown = false;
        RefreshSelectableObjects();
    }

    // Update is called once per frame
    void Update()
    {
        if (Draggable.DraggingThis != null)
        {
            // Un drag (carte ou créature) est en cours : on annule/empêche le cadre de sélection
            isMouseDown = false;
            if (isDragging)
            {
                isDragging = false;
                selectionBox.gameObject.SetActive(false);
            }
            return;
        }

        if (groupMoveActive)
        {
            if (Input.GetMouseButtonDown(0))
                ConfirmGroupMove();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            isMouseDown = true;
            MouseStartPos = Input.mousePosition;
            RefreshSelectableObjects();
            foreach (OneCreatureManager so in CurrSelectedObjects)
            {
                so.Deselect();
            }
            CurrSelectedObjects.Clear();
        }
        if (isMouseDown)
        {
            if (Vector3.Distance(Input.mousePosition, MouseStartPos) > pixelBeforeAppartion && !isDragging)
            {
                isDragging = true;
                selectionBox.gameObject.SetActive(true);
            }
            if(isDragging)
            {
                Vector2 startLocal = ScreenToLocal(MouseStartPos);
                Vector2 currentLocal = ScreenToLocal(Input.mousePosition);

                float boxWidth = currentLocal.x - startLocal.x;
                float boxHeight = currentLocal.y - startLocal.y;
                selectionBox.sizeDelta = new Vector2(Mathf.Abs(boxWidth), Mathf.Abs(boxHeight));
                selectionBox.anchoredPosition = (startLocal + currentLocal) / 2;

                SelectUnits();

            }
        }
        if (Input.GetMouseButtonUp(0))
        {
            isMouseDown = false;
            isDragging = false;
            selectionBox.gameObject.SetActive(false);

            
            if (CurrSelectedObjects.Count > 0)
                StartGroupMove();
        }
    }

    void SelectUnits()
    {
        foreach (OneCreatureManager so in AllSelectableObjects)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(so.transform.position);
            Vector2 localPos = ScreenToLocal(screenPos);

            float left   = selectionBox.anchoredPosition.x - selectionBox.sizeDelta.x / 2;
            float right  = selectionBox.anchoredPosition.x + selectionBox.sizeDelta.x / 2;
            float bottom = selectionBox.anchoredPosition.y - selectionBox.sizeDelta.y / 2;
            float top    = selectionBox.anchoredPosition.y + selectionBox.sizeDelta.y / 2;

            if (localPos.x > left && localPos.x < right && localPos.y > bottom && localPos.y < top)
            {
                if (!CurrSelectedObjects.Contains(so))
                {
                    CurrSelectedObjects.Add(so);
                    so.Select();
                }
            }
            else
            {
                if (CurrSelectedObjects.Contains(so))
                {
                    CurrSelectedObjects.Remove(so);
                    so.Deselect();
                }
            }
        }
    }

    void RefreshSelectableObjects()
    {
        AllSelectableObjects.Clear();
        Player localPlayer = GlobalSettings.Instance.localPlayer;
        if (localPlayer == null) return;

        foreach (CreatureLogic cl in localPlayer.playedCards.Creatures)
        {
            if (!cl.CanMove) continue;

            GameObject go = IDHolder.GetGameObjectWithID(cl.UniqueCreatureID);
            if (go == null) continue;
            OneCreatureManager ocm = go.GetComponent<OneCreatureManager>();
            if (ocm != null) AllSelectableObjects.Add(ocm);
        }
    }

    void StartGroupMove()
    {
        HashSet<PlayerArea> reachable = new HashSet<PlayerArea>();
        foreach (OneCreatureManager so in CurrSelectedObjects)
        {
            DragCreatureActions dca = so.GetComponentInChildren<DragCreatureActions>();
            if (dca != null) dca.GetReachableAreasInto(reachable);
        }

        if (reachable.Count == 0) return;

        groupMoveActive = true;
        highlightedAreas.Clear();
        foreach (PlayerArea pa in reachable)
        {
            pa.tableVisual.SetHighlight(true);
            highlightedAreas.Add(pa);
        }

        Vector3 centroid = Vector3.zero;
        foreach (OneCreatureManager so in CurrSelectedObjects)
            centroid += so.transform.position;
        centroid /= CurrSelectedObjects.Count;

        groupMoveArrow.transform.position = centroid;
        groupMoveArrow.ShowToMouse();
    }

    void ConfirmGroupMove()
    {
        Player localPlayer = GlobalSettings.Instance.localPlayer;
        PlayerArea targetArea = localPlayer != null ? localPlayer.SelectedPArea() : null;

        if (targetArea != null && highlightedAreas.Contains(targetArea))
        {
            int movedCount = 0;
            foreach (OneCreatureManager so in CurrSelectedObjects)
            {
                DragCreatureActions dca = so.GetComponentInChildren<DragCreatureActions>();
                if (dca != null && dca.TryGroupMoveTo(targetArea))
                    movedCount++;
            }
            if (movedCount < CurrSelectedObjects.Count)
                new ShowMessageCommand("Not all Units could reach that zone.", 1f).AddToQueue();
        }

        EndGroupMove();
    }

    void EndGroupMove()
    {
        groupMoveActive = false;
        groupMoveArrow.Hide();
        foreach (PlayerArea pa in highlightedAreas)
            pa.tableVisual.SetHighlight(false);
        highlightedAreas.Clear();

        foreach (OneCreatureManager so in CurrSelectedObjects)
            so.Deselect();
        CurrSelectedObjects.Clear();
    }


}
