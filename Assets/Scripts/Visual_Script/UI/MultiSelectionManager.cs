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

    // Vrai tant qu'un clic de confirmation de group-move/board est attendu — même convention que
    // OnPlayTargetingSession.IsActive (voir DragSpellNoTarget/DragSpellOnTarget.CanDrag) : empêche
    // Draggable.OnMouseDown() de détourner ce clic en un nouveau drag sur la créature/le transport
    // survolé (ex: cliquer sur le transport ciblé le fait sinon glisser lui-même au lieu de confirmer
    // l'embarquement du groupe dessus).
    public static bool IsConfirmingGroupMove;

    bool isMouseDown, isDragging, groupMoveActive;
    Vector3 MouseStartPos;
    readonly List<PlayerArea> highlightedAreas = new List<PlayerArea>();
    readonly List<OneCreatureManager> boardableTransports = new List<OneCreatureManager>();
    // Suivi en continu (comme DragCreatureActions.UpdateHoveredTransport) plutôt qu'un test ponctuel
    // au moment du clic : sans retour visuel pendant le survol, le joueur n'a aucun moyen de savoir
    // s'il est précisément sur la petite hitbox du transport avant de cliquer.
    OneCreatureManager hoveredGroupTransport;

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
        // ScreenToLocal() renvoie des coordonnées relatives au pivot du parent :
        // les ancres du selectionBox doivent donc être centrées (0.5, 0.5) pour matcher ce référentiel,
        // sinon anchoredPosition subit un décalage constant égal à la moitié de la taille du parent.
        selectionBox.anchorMin = selectionBox.anchorMax = new Vector2(0.5f, 0.5f);
        RefreshSelectableObjects();
    }

    // Update is called once per frame
    void Update()
    {
        if (Draggable.DraggingThis != null || OnPlayTargetingSession.IsActive
            || (ChooseOneManager.Instance != null && ChooseOneManager.Instance.AnyPending))
        {
            // Un drag (carte ou créature) est en cours, ou une session de ciblage de sort est active
            // (le drag "souris" est coupé court dès OnStartDrag par DragSpellOnTarget.EndDragSilently,
            // alors que le bouton reste enfoncé) : on annule/empêche le cadre de sélection.
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
            UpdateGroupMoveHover();
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

    static readonly Vector3[] _cardWorldCorners = new Vector3[4];

    void SelectUnits()
    {
        Vector2 boxMin = Vector2.Min(MouseStartPos, Input.mousePosition);
        Vector2 boxMax = Vector2.Max(MouseStartPos, Input.mousePosition);

        foreach (OneCreatureManager so in AllSelectableObjects)
        {
            RectTransform cardRect = so.frame != null ? so.frame.rectTransform : null;
            if (cardRect == null) continue;

            cardRect.GetWorldCorners(_cardWorldCorners);

            Vector2 cardMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 cardMax = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector3 screenCorner = Camera.main.WorldToScreenPoint(_cardWorldCorners[i]);
                cardMin = Vector2.Min(cardMin, screenCorner);
                cardMax = Vector2.Max(cardMax, screenCorner);
            }

            bool overlaps = cardMin.x <= boxMax.x && cardMax.x >= boxMin.x
                          && cardMin.y <= boxMax.y && cardMax.y >= boxMin.y;

            if (overlaps)
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
        HashSet<OneCreatureManager> boardable = new HashSet<OneCreatureManager>();
        foreach (OneCreatureManager so in CurrSelectedObjects)
        {
            DragCreatureActions dca = so.GetComponentInChildren<DragCreatureActions>();
            if (dca != null)
            {
                dca.GetReachableAreasInto(reachable);
                dca.GetBoardableTransportsInto(boardable);
            }
        }

        List<string> boardableNames = new List<string>();
        foreach (OneCreatureManager t in boardable)
            boardableNames.Add(t.cardAsset != null ? t.cardAsset.name : "?");
        Debug.Log($"[Transport] StartGroupMove — {CurrSelectedObjects.Count} selected, reachable={reachable.Count}, boardable={boardable.Count} [{string.Join(", ", boardableNames)}]");

        if (reachable.Count == 0 && boardable.Count == 0) return;

        groupMoveActive = true;
        IsConfirmingGroupMove = true;
        highlightedAreas.Clear();
        foreach (PlayerArea pa in reachable)
        {
            pa.tableVisual.SetHighlight(true);
            highlightedAreas.Add(pa);
        }

        boardableTransports.Clear();
        foreach (OneCreatureManager t in boardable)
        {
            t.SetTransportHighlight(true);
            boardableTransports.Add(t);
        }

        Vector3 centroid = Vector3.zero;
        foreach (OneCreatureManager so in CurrSelectedObjects)
            centroid += so.transform.position;
        centroid /= CurrSelectedObjects.Count;

        groupMoveArrow.transform.position = centroid;
        groupMoveArrow.ShowToMouse();
    }

    // Survole les transports embarquables chaque frame pendant groupMoveActive (bounds écran, voir
    // OneCreatureManager.IsScreenPointOver) — même technique que DragCreatureActions.UpdateHoveredTransport,
    // pour donner un retour visuel (vert) avant le clic plutôt qu'un test ponctuel au moment du clic.
    void UpdateGroupMoveHover()
    {
        OneCreatureManager newHover = null;
        foreach (OneCreatureManager t in boardableTransports)
        {
            if (t != null && t.IsScreenPointOver(Input.mousePosition))
            {
                newHover = t;
                break;
            }
        }

        if (newHover == hoveredGroupTransport) return;

        if (hoveredGroupTransport != null)
            hoveredGroupTransport.SetTransportHighlight(true);
        hoveredGroupTransport = newHover;
        if (hoveredGroupTransport != null)
            hoveredGroupTransport.SetTransportHighlight(true, targeted: true);
    }

    void ConfirmGroupMove()
    {
        // Un transport survolé prime sur une zone : même hiérarchie que le drag simple (voir
        // DragCreatureActions.OnEndDrag), boarder le groupe plutôt que le déplacer vers une zone.
        OneCreatureManager targetTransport = hoveredGroupTransport;

        Debug.Log($"[Transport] ConfirmGroupMove — mousePos={Input.mousePosition}, boardableTransports.Count={boardableTransports.Count}, targetTransport={(targetTransport != null ? targetTransport.cardAsset?.name : "null")}");

        if (targetTransport == null)
        {
            foreach (OneCreatureManager t in boardableTransports)
                if (t != null)
                    Debug.Log($"[Transport] ConfirmGroupMove — miss detail: {t.DebugScreenBoundsInfo(Input.mousePosition)}");
        }

        if (targetTransport != null)
        {
            int boardedCount = 0;
            foreach (OneCreatureManager so in CurrSelectedObjects)
            {
                DragCreatureActions dca = so.GetComponentInChildren<DragCreatureActions>();
                if (dca != null && dca.TryGroupBoardTo(targetTransport))
                    boardedCount++;
            }
            if (boardedCount < CurrSelectedObjects.Count)
                new ShowMessageCommand("Not all Units could board that transport.", 1f).AddToQueue();

            EndGroupMove();
            return;
        }

        Player localPlayer = GlobalSettings.Instance.localPlayer;
        PlayerArea targetArea = localPlayer != null ? localPlayer.SelectedPArea() : null;

        Debug.Log($"[Transport] ConfirmGroupMove — no transport hit, targetArea={(targetArea != null ? targetArea.name : "null")}, highlightedAreas.Contains={highlightedAreas.Contains(targetArea)}");

        if (targetArea != null && highlightedAreas.Contains(targetArea))
        {
            // Ordre stable (gauche→droite dans leur rangée d'origine) au lieu de l'ordre de sélection :
            // TryGroupMoveTo assigne des slots séquentiels selon CET ordre, donc le groupe arrive dans
            // la zone cible en gardant l'agencement qu'il avait avant le déplacement.
            List<OneCreatureManager> ordered = new List<OneCreatureManager>(CurrSelectedObjects);
            ordered.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

            // Un seul slot de base par rangée (mêlée / distance), calculé une fois depuis la position
            // de la souris ; chaque unité suivante de cette rangée prend le slot suivant. Voir
            // DragCreatureActions.Move() : rééchantillonner la souris à chaque unité (ancien comportement)
            // produisait un ordre incohérent, la rangée cible grandissant d'un ghost à chaque itération.
            int meleeBase = -1, rangedBase = -1;
            int meleeOffset = 0, rangedOffset = 0;
            int movedCount = 0;

            foreach (OneCreatureManager so in ordered)
            {
                DragCreatureActions dca = so.GetComponentInChildren<DragCreatureActions>();
                if (dca == null) continue;

                bool isMelee = so.cardAsset != null && so.cardAsset.melee;
                int slot;
                if (isMelee)
                {
                    if (meleeBase < 0)
                        meleeBase = targetArea.tableVisual.ToNetworkTablePos(true, targetArea.tableVisual.TablePosForNewCreature(true));
                    slot = meleeBase + meleeOffset;
                }
                else
                {
                    if (rangedBase < 0)
                        rangedBase = targetArea.tableVisual.ToNetworkTablePos(false, targetArea.tableVisual.TablePosForNewCreature(false));
                    slot = rangedBase + rangedOffset;
                }

                if (dca.TryGroupMoveTo(targetArea, slot))
                {
                    movedCount++;
                    if (isMelee) meleeOffset++; else rangedOffset++;
                }
            }
            if (movedCount < CurrSelectedObjects.Count)
                new ShowMessageCommand("Not all Units could reach that zone.", 1f).AddToQueue();
        }

        EndGroupMove();
    }

    void EndGroupMove()
    {
        groupMoveActive = false;
        IsConfirmingGroupMove = false;
        groupMoveArrow.Hide();
        foreach (PlayerArea pa in highlightedAreas)
            pa.tableVisual.SetHighlight(false);
        highlightedAreas.Clear();

        foreach (OneCreatureManager t in boardableTransports)
            if (t != null) t.SetTransportHighlight(false);
        boardableTransports.Clear();
        hoveredGroupTransport = null;

        foreach (OneCreatureManager so in CurrSelectedObjects)
            so.Deselect();
        CurrSelectedObjects.Clear();
    }


}
