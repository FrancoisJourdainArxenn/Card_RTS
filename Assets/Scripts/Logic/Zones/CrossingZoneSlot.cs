using System.Collections.Generic;
using UnityEngine;

// Doit s'enregistrer avant GlobalSettings.Awake() (ordre par défaut) pour que
// GlobalSettings.InitFromMap() puisse inclure ces zones dans Player.PAreas.
// Même mécanisme que MapManager ([DefaultExecutionOrder(-200)]) mais plus tôt.
[DefaultExecutionOrder(-300)]
public class CrossingZoneSlot : MonoBehaviour
{
    private static readonly List<CrossingZoneSlot> allSlots = new List<CrossingZoneSlot>();
    public static IReadOnlyList<CrossingZoneSlot> AllSlots => allSlots;

    public ZoneManager Zone { get; private set; }
    public ZoneCombatResolver Resolver { get; private set; }
    public bool InUse { get; private set; }

    // Par joueur impliqué dans le croisement en cours : d'où il venait et où il allait
    // à l'origine, pour pouvoir dispatcher les survivants une fois le combat résolu.
    private readonly Dictionary<Player, (int originBaseID, int intendedTargetBaseID)> memory = new();

    void Awake()
    {
        Zone = GetComponent<ZoneManager>();
        Resolver = GetComponent<ZoneCombatResolver>();
        allSlots.Add(this);

        if (Zone == null)
            Debug.LogError($"[CrossingZoneSlot] {name} n'a pas de ZoneManager/ZoneVisual sur le même GameObject.", this);
        if (Resolver == null)
            Debug.LogError($"[CrossingZoneSlot] {name} n'a pas de ZoneCombatResolver sur le même GameObject.", this);
    }

    void OnDestroy()
    {
        allSlots.Remove(this);
    }

    public static CrossingZoneSlot GetFreeSlot()
    {
        foreach (CrossingZoneSlot slot in allSlots)
            if (!slot.InUse)
                return slot;
        return null;
    }

    public void MarkInUse() => InUse = true;

    public void MarkFree()
    {
        InUse = false;
        memory.Clear();
    }

    public void RecordOriginAndDestination(Player player, int originBaseID, int intendedTargetBaseID)
        => memory[player] = (originBaseID, intendedTargetBaseID);

    public bool TryGetMemory(Player player, out int originBaseID, out int intendedTargetBaseID)
    {
        if (memory.TryGetValue(player, out var entry))
        {
            originBaseID = entry.originBaseID;
            intendedTargetBaseID = entry.intendedTargetBaseID;
            return true;
        }
        originBaseID = -1;
        intendedTargetBaseID = -1;
        return false;
    }
}
