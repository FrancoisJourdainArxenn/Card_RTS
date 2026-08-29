using System;
using System.Collections.Generic;

public class ZoneLogic : IIdentifiable
{
    public int ID { get; }
    public string DisplayName { get; }
    public List<ZonePathLogic> AdjacentPaths { get; } = new List<ZonePathLogic>();
    private readonly Func<List<int>> _getSubZoneIDs;
    public List<int> SubZoneIDs => _getSubZoneIDs();

    public ZoneLogic(int id, string displayName, Func<List<int>> getSubZoneIDs)
    {
        ID = id;
        DisplayName = displayName;
        _getSubZoneIDs = getSubZoneIDs;
    }

    public void AddPath(ZonePathLogic path) => AdjacentPaths.Add(path);
    public void RemovePath(ZonePathLogic path) => AdjacentPaths.Remove(path);

    public bool IsAdjacentTo(ZoneLogic other)
        => AdjacentPaths.Exists(p => p.ConnectsTo(other));
}
