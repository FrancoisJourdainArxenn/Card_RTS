using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ZoneLogic : MonoBehaviour
{
    [Header("Adjacent Zones")]
    public List<ZoneLogic> adjacentZones;

    [HideInInspector]
    public List<PlayerArea> subZones = new List<PlayerArea>();

    public int ZoneID { get; private set; }
    public List<int> subZoneIDs => subZones.Select(sz => sz.baseID).ToList();

    void Awake()
    {
        ZoneID = GetHierarchyPath(transform).GetHashCode();

        foreach (PlayerArea pa in GetComponentsInChildren<PlayerArea>())
        {
            subZones.Add(pa);
            pa.parentZone = this;
        }
    }

    public bool IsAdjacentTo(ZoneLogic other)
    {
        return adjacentZones.Contains(other);
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
