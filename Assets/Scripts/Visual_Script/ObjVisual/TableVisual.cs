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

    private bool cursorOverThisTable = false;
    private bool isFogged = false;
    private BoxCollider col;
    private int _previewIndex = -1;
    private bool _previewIsMelee;

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
        foreach (GameObject c in MeleeCreaturesOnTable)  if (c != null) c.SetActive(!fogged);
        foreach (GameObject c in RangedCreaturesOnTable) if (c != null) c.SetActive(!fogged);
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
        // Debug.Log($"[Add] {ca.name} à l'index {listIndex} — liste : [{string.Join(", ", targetList.ConvertAll(g => { var ocm = g.GetComponent<OneCreatureManager>(); return (ocm != null && ocm.cardAsset != null) ? ocm.cardAsset.name : "?"; }))}]");

        WhereIsTheCardOrCreature w = creature.GetComponent<WhereIsTheCardOrCreature>();
        w.Slot = rowLocalPos;
        w.VisualState = owner == AreaPosition.Low ? VisualStates.LowTable : VisualStates.TopTable;

        PlaceCreaturesOnNewSlots();

        if (isFogged) creature.SetActive(false);
        ownerArea?.RefreshAreaStats();
        if (completeCommand) Command.CommandExecutionComplete();
    }

    // rowLocalPos : 0 = le plus à gauche dans la rangée
    public void MoveCreatureToIndex(GameObject creature, int UniqueID, int rowLocalPos, int baseID)
    {
        var ocm      = creature.GetComponent<OneCreatureManager>();
        bool isMelee = ocm != null && ocm.cardAsset != null && ocm.cardAsset.melee;
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
        ownerArea?.RefreshAreaStats();

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
        ownerArea?.RefreshAreaStats();
        PlaceCreaturesOnNewSlots();
    }

    public void RemoveCreatureWithID(int IDToRemove)
    {
        GameObject creatureToRemove = IDHolder.GetGameObjectWithID(IDToRemove);
        if (!MeleeCreaturesOnTable.Remove(creatureToRemove))
            RangedCreaturesOnTable.Remove(creatureToRemove);
        Destroy(creatureToRemove);

        PlaceCreaturesOnNewSlots();
        ownerArea?.RefreshAreaStats();
        Command.CommandExecutionComplete();
    }

    void PlaceCreaturesOnNewSlots()
    {
        int meleeGap  = (_previewIndex >= 0 &&  _previewIsMelee) ? _previewIndex : -1;
        int rangedGap = (_previewIndex >= 0 && !_previewIsMelee) ? _previewIndex : -1;

        PlaceRowOnSlots(MeleeCreaturesOnTable,  GetRowSlots(true), meleeGap);
        PlaceRowOnSlots(RangedCreaturesOnTable, rangedSlots,       rangedGap);
    }

    void PlaceRowOnSlots(List<GameObject> group, CenteredSlots rowSlots, int gapIndex = -1)
    {
        int count = group.Count;
        if (count == 0) return;

        bool hasGap = gapIndex >= 0 && gapIndex <= count;
        int virtualCount = count + (hasGap ? 1 : 0);

        for (int i = 0; i < count; i++)
        {
            int virtualIndex = (hasGap && i >= gapIndex) ? i + 1 : i;
            Vector3 targetPos = rowSlots.GetSlotPosition(virtualIndex, virtualCount);
            group[i].transform.DOKill();
            group[i].transform.DOMove(targetPos, 0.3f).SetEase(Ease.OutQuad);
        }
    }

    public void ShowInsertPreview(int rowLocalPos, bool isMelee)
    {
        _previewIndex   = rowLocalPos;
        _previewIsMelee = isMelee;
        PlaceCreaturesOnNewSlots();
    }

    public void ClearInsertPreview()
    {
        if (_previewIndex < 0) return;
        _previewIndex = -1;
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
        creature.GetComponent<OneCreatureManager>().SetGray(true);
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

    public void SetOwnerColor(Color color) => glow.GetComponent<Image>().color = color;
}
