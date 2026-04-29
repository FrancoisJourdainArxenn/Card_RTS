using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ZoneView : MonoBehaviour
{
    [Header("Adjacent Zones")]
    public List<ZoneView> adjacentZones;

    [HideInInspector]
    public List<PlayerArea> subZones = new List<PlayerArea>();

    public ZoneLogic Logic { get; private set; }

    void Awake()
    {
        int id = GetHierarchyPath(transform).GetHashCode();
        Logic = new ZoneLogic(id, () => subZones.Select(sz => sz.baseID).ToList());

        foreach (PlayerArea pa in GetComponentsInChildren<PlayerArea>())
        {
            subZones.Add(pa);
            pa.parentZone = this;
        }
    }

    void Start()
    {
        Logic.SetAdjacentZones(adjacentZones.ConvertAll(z => z.Logic));
    }

    public bool IsAdjacentTo(ZoneView other) => Logic.IsAdjacentTo(other.Logic);

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
