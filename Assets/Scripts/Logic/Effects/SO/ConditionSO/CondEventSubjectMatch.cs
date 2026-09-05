using UnityEngine;
using UnityEngine.Serialization;

// Condition générique sur le sujet d'un trigger réactif (EventSubjectCreature / EventSubjectBuilding) :
// l'allié qui vient de mourir, la créature qui vient d'être jouée/créée, etc.
//
// À la différence de CondEntityCount (qui scanne le plateau vivant via TargetFunctions), celle-ci
// inspecte directement le sujet de l'événement, y compris s'il est en train de mourir — les scans
// de plateau excluent les entités pending-death (cf. TargetFunctions.cs), ce qui les rend inutilisables
// pour un trigger comme OnFriendlyCreatureDies où le sujet est justement en train de disparaître.
//
// Remplace CondMelee / CondMyZone / CondSubType et leurs combinaisons via MultipleCond (ex: SoldierInMyZone) :
// tout se règle depuis l'inspecteur (team, melee, même zone que la source, filtre de carte).
[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Event Subject Match")]
public class CondEventSubjectMatch : ConditionSO
{
    public enum SubjectSource
    {
        Entity,     // EventSubjectCreature / EventSubjectBuilding — une entité du plateau
        PlayedCard, // context.PlayedCard — un sort/action joué (OnActionPlayed), sans entité de plateau
    }

    public enum EncounterCombatTarget
    {
        EventSubject, // le sujet de l'évènement (EventSubjectCreature/Building) — cohérent avec le reste de la condition
        Source,       // context.Source — l'unité qui porte l'effet/trigger
        Either        // vrai si l'un des deux est dans un combat de rencontre
    }

    [Header("Subject")]
    public SubjectSource subjectSource = SubjectSource.Entity;

    [Header("Team")]
    public bool filterByTeam;
    public TargetTeam requiredTeam = TargetTeam.Friendly;

    [Header("Melee / Ranged")]
    [FormerlySerializedAs("filterByMelee")]
    public bool filterByType;
    [FormerlySerializedAs("requiredMelee")]
    public bool isMelee = false;

    [Header("Zone")]
    public bool requireSameZoneAsSource;

    [Header("Encounter Combat")]
    public bool requireEncounterZone;
    public EncounterCombatTarget encounterCombatTarget = EncounterCombatTarget.EventSubject;
    [Tooltip("Si coché, exige aussi qu'un combat soit réellement en cours ce round dans la zone (HasPossibleCombat). Si décoché, seul le fait d'être dans une zone de rencontre compte.")]
    public bool requireActiveCombatThisRound = false;

    [Header("Card")]
    public CardFilterSO cardFilter;

    public override bool Evaluate(EffectContext context)
    {
        // Sort/action joué : ni Team, ni Melee/Ranged, ni Zone, ni Encounter Combat n'ont de sens
        // pour une carte sans entité de plateau — seul le filtre de carte s'applique.
        if (subjectSource == SubjectSource.PlayedCard)
        {
            if (requireEncounterZone) return false;
            return context.PlayedCard != null && (cardFilter == null || cardFilter.Matches(context.PlayedCard));
        }

        CreatureLogic creature = context.EventSubjectCreature;
        BuildingLogic building = context.EventSubjectBuilding;
        ILivable subject = (ILivable)creature ?? (ILivable)building;

        // Un trigger "direct" (ex: OnBattleStart) n'a pas de sujet d'évènement — seul context.Source
        // existe. On ne bloque donc que si un filtre a réellement besoin du sujet (tout sauf un check
        // Encounter Combat ciblant uniquement la Source).
        bool needsSubject = filterByTeam || filterByType || requireSameZoneAsSource || cardFilter != null
            || (requireEncounterZone && encounterCombatTarget != EncounterCombatTarget.Source);
        if (needsSubject && subject == null) return false;

        CardAsset ca = creature != null ? creature.ca : building?.ca;

        if (filterByTeam)
        {
            Player subjectOwner = creature != null ? creature.owner : building?.owner;
            bool isFriendly = subjectOwner != null && subjectOwner == context.Owner;
            if (requiredTeam == TargetTeam.Friendly && !isFriendly) return false;
            if (requiredTeam == TargetTeam.Enemy && isFriendly) return false;
        }

        if (filterByType && (ca == null || ca.melee != isMelee))
            return false;

        if (requireSameZoneAsSource && (context.Source?.Zone == null || subject.Zone != context.Source.Zone))
            return false;

        if (requireEncounterZone)
        {
            bool subjectOk = encounterCombatTarget != EncounterCombatTarget.Source && IsInEncounterCombat(subject);
            bool sourceOk  = encounterCombatTarget != EncounterCombatTarget.EventSubject && IsInEncounterCombat(context.Source);
            if (!subjectOk && !sourceOk) return false;
        }

        if (cardFilter != null && !cardFilter.Matches(ca))
            return false;

        return true;
    }

    bool IsInEncounterCombat(ILivable livable)
    {
        ZoneCombatResolver resolver = livable switch
        {
            CreatureLogic creature => ZoneCombatResolver.FindForBase(creature.BaseID),
            BuildingLogic building => ZoneCombatResolver.FindForBuilding(building),
            _ => null,
        };
        if (resolver == null || !resolver.isEncounterZone) return false;
        return !requireActiveCombatThisRound || resolver.HasPossibleCombat();
    }
}
