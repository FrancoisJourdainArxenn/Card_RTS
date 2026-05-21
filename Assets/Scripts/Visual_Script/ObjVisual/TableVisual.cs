using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine.UI;

public class TableVisual : MonoBehaviour
{
    public AreaPosition owner;
    public SameDistanceChildren rangedSlots;       // rangée ranged (non-melee)
    public SameDistanceChildren meleeSlots;  // rangée melee
    public SameDistanceChildren pendingSlots;
    public GameObject glow;
    public Color ownerColor;
    [SerializeField] public LayerMask tableRaycastMask;
    [SerializeField] public List<GameObject> MeleeCreaturesOnTable  = new List<GameObject>();
    [SerializeField] public List<GameObject> RangedCreaturesOnTable = new List<GameObject>();
    [SerializeField] public List<GameObject> PendingCreaturesOnTable = new List<GameObject>();
    [HideInInspector] public PlayerArea ownerArea;

    private float initialSlotsLocalPosX;
    private float initialMeleeSlotsLocalPosX;
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
        if (rangedSlots != null)     initialSlotsLocalPosX      = rangedSlots.transform.localPosition.x;
        if (meleeSlots != null) initialMeleeSlotsLocalPosX = meleeSlots.transform.localPosition.x;
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
        SameDistanceChildren rowSlots = GetRowSlots(isMelee);
        List<GameObject> targetList   = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;

        int listIndex   = Mathf.Min(rowLocalPos, targetList.Count);
        int newCount    = targetList.Count + 1;
        int slotCount   = rowSlots.Children.Length;
        int firstSlot   = (slotCount - newCount) / 2;
        int lastSlot    = firstSlot + newCount - 1;
        int spawnSlot   = Mathf.Clamp(lastSlot - listIndex, 0, slotCount - 1);
        Vector3 spawnPos = rowSlots.Children[spawnSlot].transform.position;

        GameObject creature = CreateCreatureGO(ca, UniqueID, baseID, spawnPos);
        creature.transform.SetParent(rowSlots.transform);
        targetList.Insert(listIndex, creature);

        WhereIsTheCardOrCreature w = creature.GetComponent<WhereIsTheCardOrCreature>();
        w.Slot = rowLocalPos;
        w.VisualState = owner == AreaPosition.Low ? VisualStates.LowTable : VisualStates.TopTable;

        ShiftSlotsGameObjectAccordingToNumberOfCreatures();
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
        SameDistanceChildren rowSlots = GetRowSlots(isMelee);
        List<GameObject> targetList   = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;

        creature.transform.SetParent(rowSlots.transform);
        targetList.Insert(Mathf.Min(rowLocalPos, targetList.Count), creature);

        WhereIsTheCardOrCreature w = creature.GetComponent<WhereIsTheCardOrCreature>();
        w.Slot = rowLocalPos;
        w.VisualState = owner == AreaPosition.Low ? VisualStates.LowTable : VisualStates.TopTable;

        ShiftSlotsGameObjectAccordingToNumberOfCreatures();
        PlaceCreaturesOnNewSlots();

        creature.SetActive(!isFogged);
        ownerArea?.RefreshAreaStats();

        // Resync l'ordre logique de combat après un repositionnement
        ownerArea?.GetOwnerPlayer()?.ResyncCreatureOrderForArea(
            baseID, MeleeCreaturesOnTable, RangedCreaturesOnTable);

        Command.CommandExecutionComplete();
    }

    // Retourne la position dans la rangée (0 = le plus à gauche)
    public int TablePosForNewCreature(float mouseX, bool isMelee)
    {
        SameDistanceChildren rowSlots = GetRowSlots(isMelee);
        int count = isMelee ? MeleeCreaturesOnTable.Count : RangedCreaturesOnTable.Count;
        return RowPosForMouse(mouseX, count, rowSlots);
    }

    private int RowPosForMouse(float mouseX, int count, SameDistanceChildren rowSlots)
    {
        if (count == 0) return 0;
        int slotCount = rowSlots.Children.Length;
        int firstSlot = (slotCount - count) / 2;
        int lastSlot  = firstSlot + count - 1;
        // Slots are left-to-right: Children[firstSlot]=leftmost, Children[lastSlot]=rightmost
        // list[0] is at Children[lastSlot] (rightmost), list[count-1] at Children[firstSlot] (leftmost)
        float leftmostX  = rowSlots.Children[firstSlot].transform.position.x;
        float rightmostX = rowSlots.Children[lastSlot].transform.position.x;

        if (mouseX <= leftmostX)  return count;  // left of all  → insert at leftmost (list end)
        if (mouseX >= rightmostX) return 0;       // right of all → insert at rightmost (list start)

        for (int j = 0; j < count - 1; j++)
        {
            float leftX  = rowSlots.Children[lastSlot - j - 1].transform.position.x;
            float rightX = rowSlots.Children[lastSlot - j].transform.position.x;
            if (mouseX >= leftX && mouseX <= rightX)
                return j + 1;
        }
        return 0;
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

        ShiftSlotsGameObjectAccordingToNumberOfCreatures();
        PlaceCreaturesOnNewSlots();
        ownerArea?.RefreshAreaStats();
        Command.CommandExecutionComplete();
    }

    void ShiftSlotsGameObjectAccordingToNumberOfCreatures()
    {
        rangedSlots.gameObject.transform.DOLocalMoveX(initialSlotsLocalPosX, 0.0f);
        if (meleeSlots != null)
            meleeSlots.gameObject.transform.DOLocalMoveX(initialMeleeSlotsLocalPosX, 0.0f);
    }

    void PlaceCreaturesOnNewSlots()
    {
        int meleeGap  = (_previewIndex >= 0 &&  _previewIsMelee) ? _previewIndex : -1;
        int rangedGap = (_previewIndex >= 0 && !_previewIsMelee) ? _previewIndex : -1;
        PlaceRowOnSlots(MeleeCreaturesOnTable,  GetRowSlots(true), meleeGap);
        PlaceRowOnSlots(RangedCreaturesOnTable, rangedSlots,       rangedGap);
    }

    void PlaceRowOnSlots(List<GameObject> group, SameDistanceChildren rowSlots, int gapIndex = -1)
    {
        int count     = group.Count;
        int slotCount = rowSlots.Children.Length;
        if (count == 0 || slotCount == 0) return;

        bool hasGap      = gapIndex >= 0 && gapIndex <= count;
        int virtualCount = hasGap ? count + 1 : count;
        int firstSlot    = (slotCount - virtualCount) / 2;
        int lastSlot     = firstSlot + virtualCount - 1;

        for (int i = 0; i < count; i++)
        {
            int virtualIndex = (hasGap && i >= gapIndex) ? i + 1 : i;
            int targetSlot   = Mathf.Clamp(lastSlot - virtualIndex, 0, slotCount - 1);
            group[i].transform.DOKill();
            group[i].transform.DOLocalMove(rowSlots.Children[targetSlot].transform.localPosition, 0.3f)
                .SetEase(Ease.OutQuad);
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
        GetRowSlots(isMelee).Children[0].transform.position.z;

    public void AddCreatureToPendingZone(CardAsset ca, int uniqueID, int baseID)
    {
        int index = pendingSlots.Children.Length / 2 + PendingCreaturesOnTable.Count;
        GameObject creature = CreateCreatureGO(ca, uniqueID, baseID, pendingSlots.Children[index].transform.position);
        creature.transform.SetParent(pendingSlots.transform);
        PendingCreaturesOnTable.Add(creature);
        creature.GetComponent<OneCreatureManager>().SetGray(true);
    }

    private SameDistanceChildren GetRowSlots(bool isMelee) =>
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
