using System;
using System.Collections.Generic;

public class ZoneLogic : IIdentifiable
{
    public int ID { get; }
    public string DisplayName { get; }
    public List<ZoneLogic> AdjacentZones { get; private set; } = new List<ZoneLogic>();
    private readonly Func<List<int>> _getSubZoneIDs;
    public List<int> SubZoneIDs => _getSubZoneIDs();

    public ZoneLogic(int id, string displayName, Func<List<int>> getSubZoneIDs)
    {
        ID = id;
        DisplayName = displayName;
        _getSubZoneIDs = getSubZoneIDs;
    }

    internal void SetAdjacentZones(List<ZoneLogic> zones) => AdjacentZones = zones;

    public bool IsAdjacentTo(ZoneLogic other) => AdjacentZones.Contains(other);
}