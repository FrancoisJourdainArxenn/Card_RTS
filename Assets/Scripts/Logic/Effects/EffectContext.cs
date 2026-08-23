using System.Collections.Generic;
using System.Linq;

public partial class EffectContext
{
    public Player Caster;
    public ILivable Target;
    public ZoneLogic TargetedZone;
    public ILivable Source;
    public CardAsset PlayedCard; // la carte jouée/lancée à l'origine de cette résolution — renseigné par EffectRegistry.ETB
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

    // excludeSource : GetEligibleTargets et GetSingleTargetAffectedElements retirent tous les deux
    // la source du pool sauf demande explicite (includesSource) — cf. le commentaire plus bas sur
    // le bug Flag-Bearer. GetTargetCount ne le faisait pas, ce qui fausse un compte du type "combien
    // d'alliés du même sous-type dans ma zone" quand la source elle-même matche le filtre.
    public int GetTargetCount(EffectObjectType type, List<TargetQuery> queries, bool excludeSource = false)
    {
        List<IIdentifiable> resolved = ResolveByType(type, queries);
        if (excludeSource && Source != null)
            resolved = resolved.Where(e => !Equals(e, Source)).ToList();
        return resolved.Count;
    }

    public int GetSourceShieldValue() => Source is CreatureLogic c ? c.ShieldValue : 0;

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
            if (affectedElement.includesEventSubject)
            {
                var subject = GetEventSubjectByType(affectedElement.affectedElementType);
                if (subject != null) elements.Add(subject);
            }

            List<IIdentifiable> queried = ResolveByType(affectedElement.affectedElementType, affectedElement.queries, targetZone);
            // Symétrique avec GetEligibleTargets (premier stage de ciblage) : sans includesSource, la
            // source ne doit pas se retrouver dans le pool via les queries de repartition (ex: un
            // effet "ally, same zone as source" repêchait la source elle-même ici, faute d'exclusion —
            // absente uniquement de CE second stage, jamais du premier). Observé concrètement sur
            // Flag-Bearer 1 (OnAttack "Inspire", RandomSingleTarget) : le pool éligible incluait
            // Flag-Bearer lui-même en plus de ses alliés, faussant le tirage aléatoire.
            if (!affectedElement.includesSource && Source != null)
                queried = queried.Where(e => !Equals(e, Source)).ToList();
            elements.AddRange(queried);
        }
        return elements;
    }

    // Returns the entity that raised the current event (e.g. the token that was just created).
    private IIdentifiable GetEventSubjectByType(EffectObjectType type) => type switch
    {
        EffectObjectType.Creature => EventSubjectCreature,
        EffectObjectType.Building => EventSubjectBuilding,
        _                         => null
    };
}
