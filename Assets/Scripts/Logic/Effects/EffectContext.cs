using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
    public List<IIdentifiable> GetExecutionTargets(EffectTargetInfo targetInfo)
    {
        if (targetInfo.requiresPlayerSelection)
        {
            if (SelectedTarget != null) return new List<IIdentifiable> { SelectedTarget };
            return new List<IIdentifiable>();
        }
        return GetEligibleTargets(targetInfo);
    }

    public List<IIdentifiable> GetEligibleTargets(EffectTargetInfo targetInfo)
    {
        List<IIdentifiable> targets = new List<IIdentifiable>();

        switch (targetInfo.targetType)
        {
            case EffectObjectType.None:
                break;
            case EffectObjectType.Player:
                targets.AddRange(GetPlayerTargets(targetInfo.targetDetails));
                break;
            case EffectObjectType.Creature:
                targets.AddRange(GetCreatureTargets(targetInfo.targetDetails));
                break;
            case EffectObjectType.Building:
                targets.AddRange(GetBuildingTargets(targetInfo.targetDetails));
                break;
            case EffectObjectType.Base:
                targets.AddRange(GetBaseTargets(targetInfo.targetDetails));
                break;
            case EffectObjectType.Zone:
                targets.AddRange(GetZoneTargets(targetInfo.targetDetails));
                break;
        }

        return targets;
    }

    public List<ILivable> GetSingleTargetAffectedElements(
        IIdentifiable target,
        List<AffectedElement> affectedElements
    ) {
        List<ILivable> elements = new List<ILivable>();

        foreach (AffectedElement affectedElement in affectedElements)
        {
            bool isAllowed(AffectedElementModifier modifier)
            {
                if (affectedElement.affectedElementModifiers.Contains(AffectedElementModifier.All))
                    return true;
                return affectedElement.affectedElementModifiers.Contains(modifier);
            }

            switch (affectedElement.affectedElementType)
            {
                case EffectObjectType.None:
                    break;

                case EffectObjectType.Creature:
                    if (isAllowed(AffectedElementModifier.Target) && target is ILivable livableCreature)
                        elements.Add(livableCreature);
                    if (isAllowed(AffectedElementModifier.Source) && Source is CreatureLogic)
                        elements.Add(Source);
                    if (isAllowed(AffectedElementModifier.Friendly))
                        elements.AddRange(Owner.Creatures);
                    if (isAllowed(AffectedElementModifier.Enemy))
                        elements.AddRange(Opponent.Creatures);
                    // TODO handle Melee, Ranged sub-filters
                    break;

                case EffectObjectType.Building:
                    if (isAllowed(AffectedElementModifier.Target) && target is ILivable livableBuilding)
                        elements.Add(livableBuilding);
                    if (isAllowed(AffectedElementModifier.Source) && Source is BuildingLogic)
                        elements.Add(Source);
                    if (isAllowed(AffectedElementModifier.Friendly))
                        elements.AddRange(Owner.Buildings);
                    if (isAllowed(AffectedElementModifier.Enemy))
                        elements.AddRange(Opponent.Buildings);
                    // TODO Friendly/Enemy buildings need owner tracking
                    break;

                case EffectObjectType.Base:
                    if (isAllowed(AffectedElementModifier.Target) && target is ILivable livableBase)
                        elements.Add(livableBase);
                    if (isAllowed(AffectedElementModifier.Source) && Source is BaseLogic)
                        elements.Add(Source);
                    break;

                case EffectObjectType.Zone:
                    if (isAllowed(AffectedElementModifier.Target) && target is ILivable livableZone)
                        elements.Add(livableZone);
                    break;

                case EffectObjectType.Player:
                    if (isAllowed(AffectedElementModifier.Source) || isAllowed(AffectedElementModifier.Friendly))
                        elements.Add(Owner);
                    if (isAllowed(AffectedElementModifier.Enemy))
                        elements.Add(Opponent);
                    break;
            }
        }
        return FilterAffectedElements(target, elements, affectedElements);
    }

    public List<ILivable> FilterAffectedElements(
        IIdentifiable target,
        List<ILivable> unfilteredList,
        List<AffectedElement> affectedElements
    )
    {
        List<ILivable> filteredList = new(unfilteredList);
        foreach (AffectedElement affectedElement in affectedElements)
        {
            switch (affectedElement.affectedElementZone)
            {
                case AffectedElementZone.NoRestriction:
                    break;

                case AffectedElementZone.SameZoneAsSource:
                    ZoneLogic sourceZone = Source.Zone;
                    filteredList = filteredList.Where(el => el.Zone == sourceZone).ToList();
                    break;

                case AffectedElementZone.SameZoneAsTarget:
                    ZoneLogic targetZone = GetZoneOf(target);
                    filteredList = filteredList.Where(el => el.Zone == targetZone).ToList();
                    break;
            }
        }

        return filteredList;
    }

    private ZoneLogic GetZoneOf(IIdentifiable target) => target switch
    {
        ZoneLogic zone   => zone,
        ILivable livable => livable.Zone,
        _                => null
    };
}