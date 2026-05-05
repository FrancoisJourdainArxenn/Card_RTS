using System;
using System.Collections.Generic;
using System.Linq;

public partial class EffectContext
{
    private List<IIdentifiable> GetTargetsByTeam(
        List<TargetQuery> queries,
        Func<IEnumerable<IIdentifiable>> getFriendly,
        Func<IEnumerable<IIdentifiable>> getEnemy,
        ZoneLogic targetZone = null)
    {
        List<IIdentifiable> targets = new();
        foreach (TargetQuery query in queries)
        {
            IEnumerable<IIdentifiable> candidates = query.team switch
            {
                TargetTeam.All      => getFriendly().Concat(getEnemy()),
                TargetTeam.Friendly => getFriendly(),
                TargetTeam.Enemy    => getEnemy(),
                _                   => Enumerable.Empty<IIdentifiable>()
            };

            candidates = query.statusFilter switch
            {
                TargetStatusFilter.Melee  => candidates.Where(t => t is CreatureLogic c && c.IsMelee),
                TargetStatusFilter.Ranged => candidates.Where(t => t is CreatureLogic c && c.IsRanged),
                _                         => candidates
            };

            if (query.zoneFilter == TargetZoneFilter.SameZoneAsSource && Source?.Zone != null)
            {
                ZoneLogic sourceZone = Source.Zone;
                candidates = candidates.Where(t => t is ILivable livable && livable.Zone == sourceZone);
            }
            else if (query.zoneFilter == TargetZoneFilter.SameZoneAsTarget && targetZone != null)
            {
                candidates = candidates.Where(t => t is ILivable livable && livable.Zone == targetZone);
            }

            targets.AddRange(candidates);
        }
        return targets;
    }

    public List<IIdentifiable> GetPlayerTargets(List<TargetQuery> queries) =>
        GetTargetsByTeam(queries,
            getFriendly: () => new[] { (IIdentifiable)Caster },
            getEnemy:    () => new[] { (IIdentifiable)Opponent });

    public List<IIdentifiable> GetCreatureTargets(List<TargetQuery> queries, ZoneLogic targetZone = null) =>
        GetTargetsByTeam(queries,
            getFriendly: () => Caster.Creatures,
            getEnemy:    () => Opponent.Creatures,
            targetZone:  targetZone);

    public List<IIdentifiable> GetBuildingTargets(List<TargetQuery> queries, ZoneLogic targetZone = null) =>
        GetTargetsByTeam(queries,
            getFriendly: () => Caster.Buildings,
            getEnemy:    () => Opponent.Buildings,
            targetZone:  targetZone);

    public List<IIdentifiable> GetBaseTargets(List<TargetQuery> queries, ZoneLogic targetZone = null) =>
        GetTargetsByTeam(queries,
            getFriendly: () => Caster.controlledBases,
            getEnemy:    () => Opponent.controlledBases,
            targetZone:  targetZone);

    public List<IIdentifiable> GetZoneTargets(List<TargetQuery> queries, ZoneLogic targetZone = null) =>
        GetTargetsByTeam(queries,
            getFriendly: () => Caster.VisibleZones,
            getEnemy:    () => Opponent.VisibleZones,
            targetZone:  targetZone);
}
