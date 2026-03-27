using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine.UI;

public class TableVisual : MonoBehaviour 
{
    // PUBLIC FIELDS

    // an enum that mark to whish caracter this table belongs. The alues are - Top or Low
    public AreaPosition owner;

    // a referense to a game object that marks positions where we should put new Creatures
    public SameDistanceChildren slots;
    public GameObject glow;
    [SerializeField] public LayerMask tableRaycastMask; // ex: layer "Table"


    // PRIVATE FIELDS

    // list of all the creature cards on the table as GameObjects
    private List<GameObject> CreaturesOnTable = new List<GameObject>();

    // initial local X position of the slots container in the scene
    private float initialSlotsLocalPosX;

    // are we hovering over this table`s collider with a mouse
    private bool cursorOverThisTable = false;

    // A 3D collider attached to this game object
    private BoxCollider col;

    // PROPERTIES

    // returns true if we are hovering over any player`s table collider
    public static bool CursorOverSomeTable //Va devoir changer parce qu'il y aura plusieurs tables.
    {
        get
        {
            TableVisual[] allTables = GameObject.FindObjectsByType<TableVisual>(FindObjectsSortMode.None);
            foreach (TableVisual table in allTables)
            {
                if (table.CursorOverThisTable)
                {                    
                    return true;
                }

            }
            return false;
        }
    }

    // returns true only if we are hovering over this table`s collider
    public bool CursorOverThisTable
    {
        get{ return cursorOverThisTable; }
    }

    // METHODS

    // MONOBEHAVIOUR METHODS (mouse over collider detection)
    void Awake()
    {
        col = GetComponent<BoxCollider>();
        // remember where the designer placed the slots object,
        // so our centering logic is applied as an offset instead of snapping everything to the origin
        if (slots != null)
            initialSlotsLocalPosX = slots.transform.localPosition.x;
    }

    // CURSOR/MOUSE DETECTION
    void Update()
    {
        // we need to Raycast because OnMouseEnter, etc reacts to colliders on cards and cards "cover" the table
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Debug.DrawRay(ray.origin, ray.direction * 300f, Color.red);
        // create an array of RaycastHits
        RaycastHit[] hits = Physics.RaycastAll(ray, 300f, tableRaycastMask, QueryTriggerInteraction.Ignore);
        bool isHoveringThisTable = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == col)
            {
                isHoveringThisTable = true;
                break;
            }
        }
        // State used by other gameplay scripts
        cursorOverThisTable = isHoveringThisTable;
        // Visual feedback
        if (glow != null && glow.activeSelf != isHoveringThisTable)
            glow.SetActive(isHoveringThisTable);

    }
   
    // method to create a new creature and add it to the table
    public void AddCreatureAtIndex(CardAsset ca, int UniqueID ,int index)
    {
        // create a new creature from prefab
        GameObject creature = GameObject.Instantiate(GlobalSettings.Instance.CreaturePrefab, slots.Children[index].transform.position, Quaternion.identity) as GameObject;

        // apply the look from CardAsset
        OneCreatureManager manager = creature.GetComponent<OneCreatureManager>();
        manager.cardAsset = ca;
        manager.ReadCreatureFromAsset();

        // add tag according to owner
        foreach (Transform t in creature.GetComponentsInChildren<Transform>())
            t.tag = owner.ToString()+"Creature";
        
        // parent a new creature gameObject to table slots
        creature.transform.SetParent(slots.transform);

        // add a new creature to the list
        CreaturesOnTable.Insert(index, creature);

        // let this creature know about its position
        WhereIsTheCardOrCreature w = creature.GetComponent<WhereIsTheCardOrCreature>();
        w.Slot = index;
        if (owner == AreaPosition.Low)
            w.VisualState = VisualStates.LowTable;
        else
            w.VisualState = VisualStates.TopTable;

        // add our unique ID to this creature
        IDHolder id = creature.AddComponent<IDHolder>();
        id.UniqueID = UniqueID;

        // after a new creature is added update placing of all the other creatures
        ShiftSlotsGameObjectAccordingToNumberOfCreatures();
        PlaceCreaturesOnNewSlots();

        // end command execution
        Command.CommandExecutionComplete();
    }


    // returns an index for a new creature based on mousePosition
    // included for placing a new creature to any positon on the table
    public int TablePosForNewCreature(float MouseX)
    {
        // if there are no creatures or if we are pointing to the right of all creatures with a mouse.
        // right - because the table slots are flipped and 0 is on the right side.
        if (CreaturesOnTable.Count == 0 || MouseX > slots.Children[0].transform.position.x)
            return 0;
        else if (MouseX < slots.Children[CreaturesOnTable.Count - 1].transform.position.x) // cursor on the left relative to all creatures on the table
            return CreaturesOnTable.Count;
        for (int i = 0; i < CreaturesOnTable.Count; i++)
        {
            if (MouseX < slots.Children[i].transform.position.x && MouseX > slots.Children[i + 1].transform.position.x)
                return i + 1;
        }
        Debug.Log("Suspicious behavior. Reached end of TablePosForNewCreature method. Returning 0");
        return 0;
    }

    // Destroy a creature
    public void RemoveCreatureWithID(int IDToRemove)
    {
        // TODO: This has to last for some time
        // Adding delay here did not work because it shows one creature die, then another creature die. 
        // 
        //Sequence s = DOTween.Sequence();
        //s.AppendInterval(1f);
        //s.OnComplete(() =>
        //   {
                
        //    });
        GameObject creatureToRemove = IDHolder.GetGameObjectWithID(IDToRemove);
        CreaturesOnTable.Remove(creatureToRemove);
        Destroy(creatureToRemove);

        ShiftSlotsGameObjectAccordingToNumberOfCreatures();
        PlaceCreaturesOnNewSlots();
        Command.CommandExecutionComplete();
    }

    /// <summary>
    /// Shifts the slots game object according to number of creatures.
    /// </summary>
    void ShiftSlotsGameObjectAccordingToNumberOfCreatures()
    {
        // On laisse l'objet slots à la position définie dans la scène
        // et on ne le recentre plus : seul l'affectation des créatures
        // aux slots sera recalculée dans PlaceCreaturesOnNewSlots.
        slots.gameObject.transform.DOLocalMoveX(initialSlotsLocalPosX, 0.0f);
    }

    /// <summary>
    /// After a new creature is added or an old creature dies, this method
    /// shifts all the creatures and places the creatures on new slots.
    /// </summary>
    void PlaceCreaturesOnNewSlots()
    {
        int creatureCount = CreaturesOnTable.Count;
        int slotCount = slots.Children.Length;
        if (creatureCount == 0 || slotCount == 0)
            return;

        // On répartit les créatures sur une bande de slots CENTRÉE.
        // Exemple : 10 slots, 3 créatures -> elles utilisent les slots 3,4,5
        int firstSlotIndex = (slotCount - creatureCount) / 2;

        for (int i = 0; i < creatureCount; i++)
        {
            GameObject g = CreaturesOnTable[i];
            int targetSlotIndex = firstSlotIndex + i;
            targetSlotIndex = Mathf.Clamp(targetSlotIndex, 0, slotCount - 1);

            Vector3 targetLocalPos = slots.Children[targetSlotIndex].transform.localPosition;
            g.transform.DOLocalMoveX(targetLocalPos.x, 0.3f);
            // apply correct sorting order and HandSlot value for later 
            // TODO: figure out if I need to do something here:
            // g.GetComponent<WhereIsTheCardOrCreature>().SetTableSortingOrder() = CreaturesOnTable.IndexOf(g);
        }
    }

}
