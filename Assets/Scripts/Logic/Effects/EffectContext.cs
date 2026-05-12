using System.Collections.Generic;

public partial class EffectContext
{
    public Player Caster;
    public ILivable Target;
    public ZoneLogic TargetedZone;
    public ILivable Source;
    public CreatureLogic EventSubjectCreature; // la creature qui vient de mourrir ou d'être jouée
    public BuildingLogic EventSubjectBuilding; // le bâtiment qui vient de mourrir ou d'être jouée
    public TurnManager.TurnPhases CurrentPhase; // la phase actuelle du tour

    public IIdentifiable SelectedTarget; // set by BeginCombatEffectManager when player picks a target

    public Player Owner    => Caster;
    public Player Opponent => Caster?.otherPlayer;

    /// <summary>
    /// Returns targets for effect execution.
    /// If the player made a selection, returns [SelectedTarget]; otherwise returns all eligible targets.
    /// GetEligibleTargets is kept separate for displaying the selection panel.
    /// </summary>
    public List<IIdentifiable> GetExecutionAffectedElements(EffectTargetInfo targetInfo)
    {
        if (targetInfo.requiresPlayerSelection)
            return new List<IIdentifiable> { SelectedTarget };
        return GetEligibleTargets(targetInfo);
    }

    // Returns the entity that plays the role of "source" for a given object type.
    // Used to resolve includesSource on both EffectTargetInfo and AffectedElement.
    private IIdentifiable GetSourceByType(EffectObjectType type) => type switch
    {
        EffectObjectType.Creature => Source as CreatureLogic,
        EffectObjectType.Building => Source as BuildingLogic,
        EffectObjectType.Base     => Source as BaseLogic,
        EffectObjectType.Zone     => TargetedZone,
        EffectObjectType.Player   => Caster,
        _                         => null
    };

    private List<IIdentifiable> ResolveByType(EffectObjectType type, List<TargetQuery> queries, ZoneLogic targetZone = null) =>
        type switch
        {
            EffectObjectType.Creature => GetCreatureTargets(queries, targetZone),
            EffectObjectType.Building => GetBuildingTargets(queries, targetZone),
            EffectObjectType.Base     => GetBaseTargets(queries, targetZone),
            EffectObjectType.Zone     => GetZoneTargets(queries, targetZone),
            EffectObjectType.Player   => GetPlayerTargets(queries),
            _                         => new List<IIdentifiable>()
        };

    public List<IIdentifiable> GetEligibleTargets(EffectTargetInfo targetInfo)
    {
        List<IIdentifiable> targets = new();
        if (targetInfo.onlySource)
        {
            IIdentifiable source = GetSourceByType(targetInfo.targetType);
            if (source != null) targets.Add(source);
            return targets;
        }
        targets.AddRange(ResolveByType(targetInfo.targetType, targetInfo.queries));
        if (!targetInfo.includesSource && Source != null && targets.Contains(Source))
            targets.Remove(Source);

        return targets;
    }

    public List<IIdentifiable> GetSingleTargetAffectedElements(IIdentifiable target, List<AffectedElement> affectedElements)
    {
        List<IIdentifiable> elements = new();
        ZoneLogic targetZone = target is ZoneLogic z ? z : (target is ILivable l ? l.Zone : null);
        foreach (AffectedElement affectedElement in affectedElements)
        {
            if (affectedElement.includesTarget && target != null)
                elements.Add(target);
            if (affectedElement.includesSource)
            {
                var src = GetSourceByType(affectedElement.affectedElementType);
                if (src != null) elements.Add(src);
            }
            elements.AddRange(ResolveByType(affectedElement.affectedElementType, affectedElement.queries, targetZone));
        }
        return elements;
    }
}
