using System.Collections.Generic;

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

    public List<ILivable> ResolveTargets(
        TargetObjectType targetType,
        List<TargetModifier> modifierTargetType,
        TargetLocation targetLocation
    ) {
        List<ILivable> targets = new List<ILivable>();

        switch (targetType)
        {
            case TargetObjectType.Player:
                if (modifierTargetType.Contains(TargetModifier.Self))
                    targets.Add(Caster);
                else if (modifierTargetType.Contains(TargetModifier.Enemy))
                    targets.Add(Opponent);
                else if (modifierTargetType.Contains(TargetModifier.All))
                {
                    targets.Add(Caster);
                    targets.Add(Opponent);
                }
                break;
            
            case TargetObjectType.Creature:
                List<CreatureLogic> potentialTargets = new List<CreatureLogic>();
                if (modifierTargetType.Contains(TargetModifier.Self) && SourceCreature != null)
                    potentialTargets.Add(SourceCreature);
                else if (modifierTargetType.Contains(TargetModifier.All))
                {
                    potentialTargets.AddRange(Caster.Creatures);
                    potentialTargets.AddRange(Opponent.Creatures);
                }
                else if (modifierTargetType.Contains(TargetModifier.Friendly))
                    potentialTargets.AddRange(Caster.Creatures);
                else if (modifierTargetType.Contains(TargetModifier.Enemy))
                    potentialTargets.AddRange(Opponent.Creatures);
                

                if (modifierTargetType.Contains(TargetModifier.Melee))
                    potentialTargets = potentialTargets.FindAll(c => c.IsMelee);
                else if (modifierTargetType.Contains(TargetModifier.Ranged))
                    potentialTargets = potentialTargets.FindAll(c => !c.IsMelee);

                if (targetLocation == TargetLocation.SelectedZone && TargetedZone != null)
                    potentialTargets = potentialTargets.FindAll(c => TargetedZone.subZoneIDs.Contains(c.BaseID));
                targets.AddRange(potentialTargets);
                break;

            case TargetObjectType.Building:
                List<BuildingLogic> potentialBuildingTargets = new List<BuildingLogic>();
                if (modifierTargetType.Contains(TargetModifier.Self) && SourceBuilding != null)
                    potentialBuildingTargets.Add(SourceBuilding);
                else if (modifierTargetType.Contains(TargetModifier.All))
                {
                    potentialBuildingTargets.AddRange(Caster.playedCards.Buildings);
                    potentialBuildingTargets.AddRange(Opponent.playedCards.Buildings);
                }
                else if (modifierTargetType.Contains(TargetModifier.Friendly))
                    potentialBuildingTargets.AddRange(Caster.playedCards.Buildings);
                else if (modifierTargetType.Contains(TargetModifier.Enemy))
                    potentialBuildingTargets.AddRange(Opponent.playedCards.Buildings);

                if (modifierTargetType.Contains(TargetModifier.Melee))
                    potentialBuildingTargets = potentialBuildingTargets.FindAll(b => b.IsMelee);

                if (targetLocation == TargetLocation.SelectedZone && TargetedZone != null)
                    potentialBuildingTargets = potentialBuildingTargets.FindAll(b => b.OriginZoneID == TargetedZone.ZoneID);

                targets.AddRange(potentialBuildingTargets);
                break;

            case TargetObjectType.Base:
                List<BaseLogic> potentialBaseTargets = new List<BaseLogic>();
                if (modifierTargetType.Contains(TargetModifier.Self) && SourceBase != null)
                    potentialBaseTargets.Add(SourceBase);
                else if (modifierTargetType.Contains(TargetModifier.All))
                {
                    potentialBaseTargets.AddRange(Caster.controlledBases);
                    potentialBaseTargets.AddRange(Opponent.controlledBases);
                }
                else if (modifierTargetType.Contains(TargetModifier.Friendly))
                    potentialBaseTargets.AddRange(Caster.controlledBases);
                else if (modifierTargetType.Contains(TargetModifier.Enemy))
                    potentialBaseTargets.AddRange(Opponent.controlledBases);
                
                targets.AddRange(potentialBaseTargets);
                break;

        }
        return targets;
    }
}