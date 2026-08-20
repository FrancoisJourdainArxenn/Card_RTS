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

            candidates = candidates.Where(t => !(t is ILivable l && (l.IsPendingDeath || l.OnDeathResolvedInBattle)));

            candidates = query.statusFilter switch
            {
                TargetStatusFilter.Melee       => candidates.Where(t => t is ILivable c && c.IsMelee),
                TargetStatusFilter.Ranged      => candidates.Where(t => t is ILivable c && c.IsRanged),
                TargetStatusFilter.MeleeFirst  => candidates.Any(t => t is ILivable c && c.IsMelee)
                    ? candidates.Where(t => t is ILivable c && c.IsMelee)
                    : candidates,
                TargetStatusFilter.Damaged     => candidates.Where(t => t is ILivable l && l.IsDamaged),
                TargetStatusFilter.NonShielded => candidates.Where(t => t is ILivable l && !l.IsShielded),
                _                              => candidates
            };

            if (query.zoneFilter == TargetZoneFilter.SameZoneAsSource)
            {
                // Source est encore null quand on calcule les cibles éligibles AVANT que la carte
                // ne soit committée (voir OnPlayTargetingSession) : TargetedZone (zone de pose)
                // sert alors de repère de remplacement.
                ZoneLogic sourceZone = Source?.Zone ?? TargetedZone;
                if (sourceZone != null)
                    candidates = candidates.Where(t => t is ILivable livable && livable.Zone == sourceZone);
            }
            else if (query.zoneFilter == TargetZoneFilter.SameZoneAsTarget && targetZone != null)
            {
                candidates = candidates.Where(t => t is ILivable livable && livable.Zone == targetZone);
            }
            if (query.cardFilter != null)
                candidates = candidates.Where(t => query.cardFilter.Matches(GetCardAsset(t)));


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

    public List<IIdentifiable> GetZoneTargets(List<TargetQuery> queries, ZoneLogic targetZone = null)
    {
        List<IIdentifiable> targets = new();
        foreach (TargetQuery query in queries)
        {
            IEnumerable<IIdentifiable> candidates = query.team switch
            {
                // All zones in the scene — neutral zones not owned by either player must still be selectable
                TargetTeam.All      => ZoneManager.AllZones.Select(z => (IIdentifiable)z.Logic),
                TargetTeam.Friendly => Caster.VisibleZones.Cast<IIdentifiable>(),
                TargetTeam.Enemy    => Opponent.VisibleZones.Cast<IIdentifiable>(),
                _                   => Enumerable.Empty<IIdentifiable>()
            };
            targets.AddRange(candidates);
        }
        return targets;
    }
    private static CardAsset GetCardAsset(IIdentifiable target) => target switch
    {
        CreatureLogic c => c.ca,
        BuildingLogic b => b.ca,
        _               => null
    };

}
