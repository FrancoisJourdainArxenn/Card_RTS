using System.Collections.Generic;
using System.Linq;

public partial class EffectContext
{
    public Player Caster;
    public ILivable Target;
    public ZoneLogic TargetedZone;
    public ILivable Source;
    public CardAsset PlayedCard; // la carte jouée/lancée à l'origine de cette résolution — renseigné par EffectRegistry.ETB
    // UniqueCardID de l'instance de carte jouée (voir CardLogic.CardsCreatedThisGame) — seul moyen de
    // retrouver l'effet côté réseau quand Source est null (carte Action/Order jouée depuis la main, sans
    // créature sur le plateau) : voir ResolveEffectReplayKey ci-dessous et
    // Player.PlayASpellFromHand/NetworkPendingPlaySpell qui le renseignent. -1 = non applicable.
    public int PlayedCardUniqueID = -1;
    // Sous-ensemble de PlayedCard : uniquement quand l'effet qui s'exécute EST l'effet propre de cette
    // carte (résolution ETB directe). Sert de source pour les bonus d'amplificateur (Blessings) — ne doit
    // PAS être propagé aux triggers réactifs d'autres créatures, sinon leurs effets non liés au sort
    // (ex: Orteg "Empowered") héritent à tort du bonus destiné au sort lui-même.
    public CardAsset AmplifierCard;
    public CreatureLogic EventSubjectCreature; // la creature qui vient de mourrir ou d'être jouée
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
        EffectObjectType.Base     => Source as BaseLogic,
        EffectObjectType.Zone     => TargetedZone,
        EffectObjectType.Player   => Caster,
        _                         => null
    };

    private List<IIdentifiable> ResolveByType(EffectObjectType type, List<TargetQuery> queries, ZoneLogic targetZone = null) =>
        type switch
        {
            EffectObjectType.Creature => GetCreatureTargets(queries, targetZone),
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

    // Le tier se lit sur homeBaseLogic (même convention que Player.cs) : une base secondaire reste
    // toujours figée à T1.
    public int GetCasterTier() =>
        Caster?.homeBaseLogic != null ? (int)Caster.homeBaseLogic.CurrentTier : 0;

    public int GetScalingCount(EffectInfo effectInfo) => effectInfo.scalingSource switch
    {
        ScalingSource.SourceShield => GetSourceShieldValue(),
        ScalingSource.CasterTier   => GetCasterTier(),
        _                          => GetTargetCount(effectInfo.scalingQuery.targetType, effectInfo.scalingQuery.queries),
    };

    // Résout (sourceEntityID, effectIndex) pour le rejeu réseau déterministe de cet effet — utilisé par
    // ChooseOneSO/TokenGenerationSO/GenerateCardsFromPoolSO pour que EffectRegistry.GetChooseOneSO (etc.)
    // le retrouvent plus tard côté client. Priorité aux entités de plateau (créature/base) ;
    // si Source est null (carte Action/Order jouée depuis la main — voir Player.PlayASpellFromHand/
    // NetworkPendingPlaySpell), retombe sur PlayedCardUniqueID/PlayedCard. (-1, -1) si rien ne matche.
    public (int sourceEntityID, int effectIndex) ResolveEffectReplayKey(EffectSO effect)
    {
        if (Source is CreatureLogic sc && sc.ca?.Effects != null)
            return (sc.UniqueCreatureID, sc.ca.Effects.FindIndex(e => e.Effect == effect));
        if (Source is BaseLogic spb && spb.ba?.Effects != null)
            return (spb.ID, spb.ba.Effects.FindIndex(e => e.Effect == effect));
        if (PlayedCardUniqueID != -1 && PlayedCard?.Effects != null)
            return (PlayedCardUniqueID, PlayedCard.Effects.FindIndex(e => e.Effect == effect));
        return (-1, -1);
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
        _                         => null
    };
}
