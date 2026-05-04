using System;
using System.Collections.Generic;
using System.Linq;

public partial class EffectContext
{
    private List<IIdentifiable> GetTargetsByTeam(
        List<TargetDetails> targetDetails,
        Func<IEnumerable<IIdentifiable>> getSelf,
        Func<IEnumerable<IIdentifiable>> getFriendly,
        Func<IEnumerable<IIdentifiable>> getEnemy)
    {
        List<IIdentifiable> targets = new();
        foreach (TargetDetails detail in targetDetails)
        {
            IEnumerable<IIdentifiable> candidates = detail.team switch
            {
                TargetTeam.All      => getFriendly().Concat(getEnemy()),
                TargetTeam.Self     => getSelf(),
                TargetTeam.Friendly => getFriendly(),
                TargetTeam.Enemy    => getEnemy(),
                _                   => Enumerable.Empty<IIdentifiable>()
            };

            candidates = detail.statusFilter switch
            {
                TargetStatusFilter.Melee  => candidates.Where(t => t is CreatureLogic c && c.IsMelee),
                TargetStatusFilter.Ranged => candidates.Where(t => t is CreatureLogic c && c.IsRanged),
                _                         => candidates
            };

            if (detail.zoneFilter == TargetZoneFilter.SameZoneAsSource && Source?.Zone != null)
            {
                ZoneLogic sourceZone = Source.Zone;
                candidates = candidates.Where(t => t is ILivable livable && livable.Zone == sourceZone);
            }

            targets.AddRange(candidates);
        }
        return targets;
    }

    public List<IIdentifiable> GetPlayerTargets(List<TargetDetails> targetDetails) =>
        GetTargetsByTeam(targetDetails,
            getSelf:     () => new[] { (IIdentifiable)Caster },
            getFriendly: () => new[] { (IIdentifiable)Caster },
            getEnemy:    () => new[] { (IIdentifiable)Opponent });

    public List<IIdentifiable> GetCreatureTargets(List<TargetDetails> targetDetails) =>
        GetTargetsByTeam(targetDetails,
            getSelf:     () => Source is CreatureLogic ? new[] { (IIdentifiable)Source } : Enumerable.Empty<IIdentifiable>(),
            getFriendly: () => Caster.Creatures,
            getEnemy:    () => Opponent.Creatures);

    public List<IIdentifiable> GetBuildingTargets(List<TargetDetails> targetDetails) =>
        GetTargetsByTeam(targetDetails,
            getSelf:     () => Source is BuildingLogic ? new[] { (IIdentifiable)Source } : Enumerable.Empty<IIdentifiable>(),
            getFriendly: () => Caster.Buildings,
            getEnemy:    () => Opponent.Buildings);

    public List<IIdentifiable> GetBaseTargets(List<TargetDetails> targetDetails) =>
        GetTargetsByTeam(targetDetails,
            getSelf:     () => Source is BaseLogic ? new[] { (IIdentifiable)Source } : Enumerable.Empty<IIdentifiable>(),
            getFriendly: () => Caster.controlledBases,
            getEnemy:    () => Opponent.controlledBases);

    public List<IIdentifiable> GetZoneTargets(List<TargetDetails> targetDetails) =>
        GetTargetsByTeam(targetDetails,
            getSelf:     () => TargetedZone != null ? new[] { (IIdentifiable)TargetedZone } : Enumerable.Empty<IIdentifiable>(),
            getFriendly: () => Caster.VisibleZones,
            getEnemy:    () => Opponent.VisibleZones);
}
