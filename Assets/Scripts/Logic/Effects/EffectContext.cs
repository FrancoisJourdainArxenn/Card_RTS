using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EffectContext
{
    public Player Caster;
    public ILivable Target;
    public ZoneLogic TargetedZone; // non-null uniquement si RequiresZoneInput
    public CreatureLogic SourceCreature; // la créature qui est à l'origine de l'effet
    public BuildingLogic SourceBuilding; // le bâtiment qui est à l'origine de l'effet
    public BaseLogic SourceBase; // la base qui est à l'origine de l'effet
    public CreatureLogic EventSubjectCreature; // la creature qui vient de mourrir ou d'être jouée
    public BuildingLogic EventSubjectBuilding; // le bâtiment qui vient de mourrir ou d'être jouée
    public TurnManager.TurnPhases CurrentPhase; // la phase actuelle du tour

    public Player Owner    => Caster;
    public Player Opponent => Caster?.otherPlayer;

    public List<IIdentifiable> GetEligibleTargets(TargetInfo targetInfo)
    {
        List<IIdentifiable> targets = new List<IIdentifiable>();
        List<TargetModifier> modifiers = targetInfo.eligibleTargetModifiers;
        
        bool all = modifiers.Contains(TargetModifier.All);
        
        bool isAllowed(TargetModifier modifier)
        {
            return modifiers.Contains(modifier) || all;
        } 

        switch (targetInfo.targetType)
        {
            case EffectObjectType.None:
                break;
            case EffectObjectType.Player:
                if (isAllowed(TargetModifier.Self))
                    targets.Add(Caster);
                if (isAllowed(TargetModifier.Enemy))
                    targets.Add(Opponent);
                break;
            case EffectObjectType.Creature:
                if (isAllowed(TargetModifier.Self) && SourceCreature != null)
                    targets.Add(SourceCreature);
                if (isAllowed(TargetModifier.Friendly))
                    targets.AddRange(Caster.Creatures);
                if (isAllowed(TargetModifier.Enemy))
                    targets.AddRange(Opponent.Creatures);
                break;
            case EffectObjectType.Building:
                if (isAllowed(TargetModifier.Self) && SourceBuilding != null)
                    targets.Add(SourceBuilding);
                if (isAllowed(TargetModifier.Friendly))
                    targets.AddRange(Caster.playedCards.Buildings);
                if (isAllowed(TargetModifier.Enemy))
                    targets.AddRange(Opponent.playedCards.Buildings);
                break;
            case EffectObjectType.Base:
                if (isAllowed(TargetModifier.Self) && SourceBase != null)
                    targets.Add(SourceBase);
                if (isAllowed(TargetModifier.Friendly))
                    targets.AddRange(Caster.controlledBases);
                if (isAllowed(TargetModifier.Enemy))
                    targets.AddRange(Opponent.controlledBases);
                break;
            case EffectObjectType.Zone:
                if (isAllowed(TargetModifier.Self) && TargetedZone != null)
                    targets.Add(TargetedZone);
                if (isAllowed(TargetModifier.Friendly))
                    targets.AddRange(Caster.VisibleZones);
                if (isAllowed(TargetModifier.Enemy))
                    targets.AddRange(Opponent.VisibleZones);
                break;
        }
        return targets;
    }
    
    
    public List<ILivable> GetSingleTargetAffectedElements(
        ILivable target,
        List<AffectedElement> affectedElements
    ) {
        List<ILivable> elements = new List<ILivable>();
        
        
        foreach (AffectedElement affectedElement in affectedElements)
        {
            bool isAllowed (AffectedElementModifier modifier)
            {
                if (affectedElement.affectedElementModifiers.Contains(AffectedElementModifier.All))
                    return true;
                if (affectedElement.affectedElementModifiers.Contains(modifier))
                    return true;
                return false;
            }

            switch (affectedElement.affectedElementType)
            {
                case EffectObjectType.None:
                    Debug.Log("Nothing is affected");
                    break;

                case EffectObjectType.Creature:
                    if (isAllowed(AffectedElementModifier.Target) && target != null)
                        elements.Add(target);
                    if (isAllowed(AffectedElementModifier.Source) && SourceCreature != null)
                        elements.Add(SourceCreature);

                    // TODO handle specific cases of friendly, enemy, melee, ranged, etc...
                    break;
                
                case EffectObjectType.Building:
                    if (isAllowed(AffectedElementModifier.Target) && target != null)
                        elements.Add(target);
                    if (isAllowed(AffectedElementModifier.Source) && SourceBuilding != null)
                        elements.Add(SourceBuilding);
                    break;

                case EffectObjectType.Base:
                    if (isAllowed(AffectedElementModifier.Target) && target != null)
                        elements.Add(target);
                    if (isAllowed(AffectedElementModifier.Source) && SourceBase != null)
                        elements.Add(SourceBase);
                    break;

                case EffectObjectType.Zone:
                    if (isAllowed(AffectedElementModifier.Target) && target != null)
                        elements.Add(target);
                    break;

                case EffectObjectType.Player:
                    if (isAllowed(AffectedElementModifier.Source))
                        elements.Add(Caster);
                    
                    // TODO add owner to ILivable
                    if (isAllowed(AffectedElementModifier.Friendly))
                        elements.Add(Caster);
                    if (isAllowed(AffectedElementModifier.Enemy))
                        elements.Add(Opponent);
                    break;
            }
        }
        return FilterAffectedElements(target, elements, affectedElements);
    }

    public List<ILivable> FilterAffectedElements(
        ILivable target,
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
                    ZoneLogic sourceZone = SourceCreature?.Zone ?? SourceBuilding?.Zone ?? SourceBase?.Zone;
                    filteredList = filteredList.Where(el => el.Zone == sourceZone).ToList();
                    break;

                case AffectedElementZone.SameZoneAsTarget:
                    filteredList = filteredList.Where(el => el.Zone == target.Zone).ToList();
                    break;
            }
        }

        return filteredList;
    }
}