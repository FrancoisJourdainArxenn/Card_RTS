using UnityEngine;
using System.Collections.Generic;

public class ZoneLogic : MonoBehaviour
{
    [Header("Adjacent Zones")]
    public List<ZoneLogic> adjacentZones;

    [HideInInspector]
    public List<PlayerArea> subZones = new List<PlayerArea>();

    void Awake()
    {
        // Auto-populate subZones from children
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
}
