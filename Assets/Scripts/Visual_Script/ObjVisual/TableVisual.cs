using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine.UI;

public class TableVisual : MonoBehaviour
{
    public AreaPosition owner;
    public CenteredSlots rangedSlots;
    public CenteredSlots meleeSlots;
    public SameDistanceChildren pendingSlots;
    public GameObject glow;
    public Color ownerColor;
    [SerializeField] public LayerMask tableRaycastMask;
    [SerializeField] public List<GameObject> MeleeCreaturesOnTable  = new List<GameObject>();
    [SerializeField] public List<GameObject> RangedCreaturesOnTable = new List<GameObject>();
    [SerializeField] public List<GameObject> PendingCreaturesOnTable = new List<GameObject>();
    [HideInInspector] public PlayerArea ownerArea;

    //#Ghost versions when losing visions
    private bool hasBeenSeen = false;
    private List<GameObject> meleeGhostCreatures = new List<GameObject>();
    private List<GameObject> rangedGhostCreatures = new List<GameObject>();


    private bool cursorOverThisTable = false;
    private bool isFogged = false;
    private BoxCollider col;
    private int _previewIndex = -1;
    private bool _previewIsMelee;
    private GameObject _movingCreature;

    // list[0] = leftmost = attaque en premier
    public IEnumerable<GameObject> AllCreaturesOnTable =>
        MeleeCreaturesOnTable.Concat(RangedCreaturesOnTable);
    public int TotalCreatureCount =>
        MeleeCreaturesOnTable.Count + RangedCreaturesOnTable.Count;

    public static bool CursorOverSomeTable
    {
        get
        {
            foreach (TableVisual t in GameObject.FindObjectsByType<TableVisual>(FindObjectsSortMode.None))
                if (t.CursorOverThisTable) return true;
            return false;
        }
    }
    public bool CursorOverThisTable => cursorOverThisTable;

    void Awake()
    {
        col = GetComponent<BoxCollider>();
    }

    public void RefreshSlotsPositions() => PlaceCreaturesOnNewSlots();

    private static readonly RaycastHit[] _raycastBuffer = new RaycastHit[8];
    void Update()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int count = Physics.RaycastNonAlloc(ray, _raycastBuffer, 300f, tableRaycastMask, QueryTriggerInteraction.Ignore);
        cursorOverThisTable = false;
        for (int i = 0; i < count; i++)
            if (_raycastBuffer[i].collider == col) { cursorOverThisTable = true; break; }
    }

    public void SetHighlight(bool active)
    {
        if (BuildingShopVisual.IsOpen) active = false;
        if (glow == null) return;
        glow.GetComponent<Image>().color = ownerColor;
        glow.SetActive(active);
    }

    public void SetFogged(bool fogged)
    {
        isFogged = fogged;

        if(!fogged)
        {
            hasBeenSeen = true;
            DestroyGhosts();
            foreach (GameObject c in MeleeCreaturesOnTable)  if (c != null) c.SetActive(true);
            foreach (GameObject c in RangedCreaturesOnTable) if (c != null) c.SetActive(true);    
        }
        else
        {
            if(hasBeenSeen)
                CreateGhosts();
            foreach (GameObject c in MeleeCreaturesOnTable)  if (c != null) c.SetActive(false);
            foreach (GameObject c in RangedCreaturesOnTable) if (c != null) c.SetActive(false);    
        }
        
    }

    // rowLocalPos : 0 = le plus à gauche dans la rangée
    public void AddCreatureAtIndex(CardAsset ca, int UniqueID, int rowLocalPos, int baseID, bool completeCommand = true)
    {
        bool isMelee = ca.melee;
        CenteredSlots rowSlots      = GetRowSlots(isMelee);
        List<GameObject> targetList = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;

        int listIndex = Mathf.Min(rowLocalPos, targetList.Count);
        int newCount  = targetList.Count + 1;
        Vector3 spawnPos = rowSlots.GetSlotPosition(listIndex, newCount);

        GameObject creature = CreateCreatureGO(ca, UniqueID, baseID, spawnPos);
        creature.transform.SetParent(rowSlots.transform);
        targetList.Insert(listIndex, creature);

        WhereIsTheCardOrCreature w = creature.GetComponent<WhereIsTheCardOrCreature>();
        w.Slot = rowLocalPos;
        w.VisualState = owner == AreaPosition.Low ? VisualStates.LowTable : VisualStates.TopTable;

        PlaceCreaturesOnNewSlots();

        if (isFogged) creature.SetActive(false);
        // ownerArea?.RefreshAreaStats();
        if (completeCommand) Command.CommandExecutionComplete();
    }

    // rowLocalPos : 0 = le plus à gauche dans la rangée
    public void MoveCreatureToIndex(GameObject creature, int UniqueID, int rowLocalPos, int baseID)
    {
        bool isMelee = CreatureLogic.CreaturesCreatedThisGame.TryGetValue(UniqueID, out CreatureLogic cl) && cl.IsMelee;
        CenteredSlots rowSlots  = GetRowSlots(isMelee);
        List<GameObject> targetList = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;

        if (rowSlots == null)
        {
            Debug.LogError($"[TableVisual] MoveCreatureToIndex: rowSlots est null (isMelee={isMelee}). Vérifie que rangedSlots/meleeSlots sont assignés dans l'Inspecteur sur {gameObject.name}.");
            Command.CommandExecutionComplete();
            return;
        }
        creature.transform.SetParent(rowSlots.transform);
        targetList.Insert(Mathf.Min(rowLocalPos, targetList.Count), creature);

        WhereIsTheCardOrCreature w = creature.GetComponent<WhereIsTheCardOrCreature>();
        w.Slot = rowLocalPos;
        w.VisualState = owner == AreaPosition.Low ? VisualStates.LowTable : VisualStates.TopTable;

        PlaceCreaturesOnNewSlots();

        creature.SetActive(!isFogged);
        // ownerArea?.RefreshAreaStats();

        ownerArea?.GetOwnerPlayer()?.ResyncCreatureOrderForArea(
            baseID, MeleeCreaturesOnTable, RangedCreaturesOnTable);

        Command.CommandExecutionComplete();
    }

    // Retourne la position dans la rangée (0 = le plus à gauche)
    public int TablePosForNewCreature(bool isMelee)
    {
        CenteredSlots rowSlots = GetRowSlots(isMelee);
        int count = isMelee ? MeleeCreaturesOnTable.Count : RangedCreaturesOnTable.Count;
        int rowPos = RowPosForMouse(count, rowSlots);
        return rowPos;
    }

    private int RowPosForMouse(int count, CenteredSlots rowSlots)
    {
        if (count == 0) return 0;

        float leftmostX  = Camera.main.WorldToScreenPoint(rowSlots.GetSlotPosition(0,         count)).x;
        float rightmostX = Camera.main.WorldToScreenPoint(rowSlots.GetSlotPosition(count - 1, count)).x;

        float mouseX = Input.mousePosition.x;
        if (mouseX <= leftmostX)  return 0;
        if (mouseX >= rightmostX) return count;

        for (int j = 0; j < count - 1; j++)
        {
            float leftX  = Camera.main.WorldToScreenPoint(rowSlots.GetSlotPosition(j,     count)).x;
            float rightX = Camera.main.WorldToScreenPoint(rowSlots.GetSlotPosition(j + 1, count)).x;
            if (mouseX >= leftX && mouseX <= rightX)
                return j + 1;
        }

        Debug.LogWarning($"[RowPosForMouse] cas non couvert : mouseX={mouseX:F3}");
        return count;
    }

    public void MoveCreatureAway(GameObject creature)
    {
        if (!MeleeCreaturesOnTable.Remove(creature))
            RangedCreaturesOnTable.Remove(creature);
        // ownerArea?.RefreshAreaStats();
        PlaceCreaturesOnNewSlots();
    }

    public void RemoveCreatureWithID(int IDToRemove)
    {
        GameObject creatureToRemove = IDHolder.GetGameObjectWithID(IDToRemove);
        if (!MeleeCreaturesOnTable.Remove(creatureToRemove))
            RangedCreaturesOnTable.Remove(creatureToRemove);
        Destroy(creatureToRemove);

        PlaceCreaturesOnNewSlots();
        // ownerArea?.RefreshAreaStats();
        Command.CommandExecutionComplete();
    }

    void PlaceCreaturesOnNewSlots()
    {
        int meleeGap  = (_previewIndex >= 0 &&  _previewIsMelee) ? _previewIndex : -1;
        int rangedGap = (_previewIndex >= 0 && !_previewIsMelee) ? _previewIndex : -1;

        GameObject meleeExcluded  = (_movingCreature != null && MeleeCreaturesOnTable.Contains(_movingCreature))  ? _movingCreature : null;
        GameObject rangedExcluded = (_movingCreature != null && RangedCreaturesOnTable.Contains(_movingCreature)) ? _movingCreature : null;

        PlaceRowOnSlots(MeleeCreaturesOnTable,  GetRowSlots(true), meleeGap,  meleeExcluded);
        PlaceRowOnSlots(RangedCreaturesOnTable, rangedSlots,       rangedGap, rangedExcluded);
    }

    // gapIndex : position du slot virtuel vide dans la rangée effective (sans excluded)
    // excluded : créature en cours de drag, exclue du calcul de slots
    void PlaceRowOnSlots(List<GameObject> group, CenteredSlots rowSlots, int gapIndex = -1, GameObject excluded = null)
    {
        int count = group.Count;
        if (count == 0) return;

        bool hasExcluded = excluded != null && group.Contains(excluded);
        int effectiveCount = count - (hasExcluded ? 1 : 0);
        bool hasGap = gapIndex >= 0;
        int virtualCount = effectiveCount + (hasGap ? 1 : 0);
        // On clamp pour éviter que gapIndex > effectiveCount ne décale rien (gap en fin de liste)
        int clampedGap = hasGap ? Mathf.Min(gapIndex, effectiveCount) : -1;

        if (virtualCount == 0) return;

        int effectivePos = 0;
        for (int i = 0; i < count; i++)
        {
            if (group[i] == excluded) continue;
            int virtualIndex = (hasGap && effectivePos >= clampedGap) ? effectivePos + 1 : effectivePos;
            Vector3 targetPos = rowSlots.GetSlotPosition(virtualIndex, virtualCount);
            group[i].transform.DOKill();
            group[i].transform.DOMove(targetPos, 0.3f).SetEase(Ease.OutQuad);
            effectivePos++;
        }
    }

    // Preview de déplacement intra-zone : la créature est exclue de la rangée et un slot vide la remplace
    public void ShowMovePreview(GameObject creature)
    {
        _movingCreature = creature;
        bool isMelee = MeleeCreaturesOnTable.Contains(creature);
        _previewIsMelee = isMelee;
        // effectiveCount + 1 gap = count total → les positions de slots sont identiques à l'original
        _previewIndex = TablePosForNewCreature(isMelee);
        PlaceCreaturesOnNewSlots();
    }

    public void ShowInsertPreview(int rowLocalPos, bool isMelee)
    {
        _previewIndex   = rowLocalPos;
        _previewIsMelee = isMelee;
        PlaceCreaturesOnNewSlots();
    }

    private static string FormatIDs(List<GameObject> list)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            IDHolder h = list[i] != null ? list[i].GetComponent<IDHolder>() : null;
            if (i > 0) sb.Append(", ");
            sb.Append(h != null ? h.UniqueID.ToString() : "?");
        }
        return sb.ToString();
    }

    public void ReorderCreature(GameObject creature)
    {
        if (_previewIndex < 0)
            return;

        bool isMelee = MeleeCreaturesOnTable.Contains(creature);
        List<GameObject> targetList = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;

        string before = FormatIDs(targetList);
        int insertIndex = _previewIndex;
        targetList.Remove(creature);
        targetList.Insert(Mathf.Min(insertIndex, targetList.Count), creature);
        Debug.Log($"[Reorder Local] {(isMelee ? "mêlée" : "distance")} | avant=[{before}] → après=[{FormatIDs(targetList)}]");

        _previewIndex = -1;
        _movingCreature = null;

        // Téléporte immédiatement la créature à sa position finale pour qu'elle réapparaisse au bon endroit
        // CenteredSlots rowSlots = GetRowSlots(isMelee);
        // int finalIndex = targetList.IndexOf(creature);
        // creature.transform.DOKill();
        // creature.transform.position = rowSlots.GetSlotPosition(finalIndex, targetList.Count);

        PlaceCreaturesOnNewSlots();

        ownerArea?.GetOwnerPlayer()?.ResyncCreatureOrderForArea(
            ownerArea.baseID, MeleeCreaturesOnTable, RangedCreaturesOnTable);
    }

    public void ApplyCreatureOrder(int[] meleeIDs, int[] rangedIDs)
    {
        SortListByIDs(MeleeCreaturesOnTable, meleeIDs);
        SortListByIDs(RangedCreaturesOnTable, rangedIDs);
        PlaceCreaturesOnNewSlots();
        ownerArea?.GetOwnerPlayer()?.ResyncCreatureOrderForArea(
            ownerArea.baseID, MeleeCreaturesOnTable, RangedCreaturesOnTable);
    }

    private void SortListByIDs(List<GameObject> list, int[] ids)
    {
        List<GameObject> sorted = new List<GameObject>(ids.Length);
        foreach (int id in ids)
        {
            GameObject go = IDHolder.GetGameObjectWithID(id);
            if (go != null && list.Contains(go))
                sorted.Add(go);
        }
        foreach (GameObject go in list)
            if (!sorted.Contains(go))
                sorted.Add(go);
        list.Clear();
        list.AddRange(sorted);
    }

    public void ClearInsertPreview()
    {
        if (_previewIndex < 0 && _movingCreature == null)
            return;
        _previewIndex = -1;
        _movingCreature = null;
        PlaceCreaturesOnNewSlots();
    }

    public float GetRowWorldZ(bool isMelee) =>
        GetRowSlots(isMelee).transform.position.z;

    public void AddCreatureToPendingZone(CardAsset ca, int uniqueID, int baseID)
    {
        int index = pendingSlots.Children.Length / 2 + PendingCreaturesOnTable.Count;
        GameObject creature = CreateCreatureGO(ca, uniqueID, baseID, pendingSlots.Children[index].transform.position);
        creature.transform.SetParent(pendingSlots.transform);
        PendingCreaturesOnTable.Add(creature);
        OneCreatureManager manager = creature.GetComponent<OneCreatureManager>();
        manager.SetGray(true);
        manager.CanReorderNow = true;
        manager.UpdateGlow();
    }

    private CenteredSlots GetRowSlots(bool isMelee) =>
        (isMelee && meleeSlots != null) ? meleeSlots : rangedSlots;

    private GameObject CreateCreatureGO(CardAsset ca, int uniqueID, int baseID, Vector3 position)
    {
        GameObject creature = GameObject.Instantiate(GlobalSettings.Instance.CreaturePrefab, position, Quaternion.identity);
        OneCreatureManager manager = creature.GetComponent<OneCreatureManager>();
        manager.BaseID   = baseID;
        manager.cardAsset = ca;
        manager.ReadCreatureFromAsset();
        foreach (Transform t in creature.GetComponentsInChildren<Transform>())
            t.tag = owner.ToString() + "Creature";
        IDHolder id = creature.AddComponent<IDHolder>();
        id.UniqueID = uniqueID;
        return creature;
    }

    private void CreateGhosts()
    {
        DestroyGhosts();
        SpawnGhostRow(MeleeCreaturesOnTable, meleeGhostCreatures, GetRowSlots(true));
        SpawnGhostRow(RangedCreaturesOnTable, rangedGhostCreatures, rangedSlots);
    }

    private void SpawnGhostRow(List<GameObject> row, List<GameObject> ghostList, CenteredSlots slots)
    {
        foreach (GameObject c in row)
        {
            if (c == null) continue;
            OneCreatureManager ocm = c.GetComponent<OneCreatureManager>();
            if (ocm?.cardAsset == null) continue;

            GameObject ghost = Instantiate(GlobalSettings.Instance.CreaturePrefab,
                c.transform.position, Quaternion.identity, slots.transform);

            OneCreatureManager ghostOcm = ghost.GetComponent<OneCreatureManager>();
            ghostOcm.BaseID = ocm.BaseID;
            ghostOcm.cardAsset = ocm.cardAsset;
            ghostOcm.ReadCreatureFromAsset();
            ghostOcm.HealthText.text = ocm.HealthText.text;
            ghostOcm.isGhost = true;
            ghostOcm.SetGray(true);

            foreach (Transform t in ghost.GetComponentsInChildren<Transform>())
                t.tag = owner.ToString() + "Creature";

            ghostList.Add(ghost);
        }
    }

    private void DestroyGhosts()
    {
        foreach (GameObject g in meleeGhostCreatures) if (g != null) Destroy(g);
        foreach (GameObject g in rangedGhostCreatures) if (g != null) Destroy(g);
        meleeGhostCreatures.Clear();
        rangedGhostCreatures.Clear();
    }

    void OnDestroy()
    {
        DestroyGhosts();
    }


    public void SetOwnerColor(Color color) => glow.GetComponent<Image>().color = color;
}
