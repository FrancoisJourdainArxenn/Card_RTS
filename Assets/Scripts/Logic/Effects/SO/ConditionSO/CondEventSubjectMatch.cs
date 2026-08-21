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

    [Header("Card")]
    public CardFilterSO cardFilter;

    public override bool Evaluate(EffectContext context)
    {
        CreatureLogic creature = context.EventSubjectCreature;
        BuildingLogic building = context.EventSubjectBuilding;
        ILivable subject = (ILivable)creature ?? (ILivable)building;
        if (subject == null) return false;

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

        if (cardFilter != null && !cardFilter.Matches(ca))
            return false;

        return true;
    }
}
