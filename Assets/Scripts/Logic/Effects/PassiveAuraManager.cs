using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Gère les effets Passive : actifs en continu tant que leur source est en jeu, réévalués (Condition +
/// ciblage) à chaque changement de plateau (RecomputeAll). Contrairement à TempEffectTracker (un seul
/// apply / un seul revert décidés à l'avance), une aura peut changer de cibles ou de valeur plusieurs
/// fois pendant sa vie — tout l'état appliqué est donc gardé ICI, jamais sur l'EffectSO (partagé entre
/// toutes les instances de la carte).
/// </summary>
public static class PassiveAuraManager
{
    private class Registration
    {
        public CardEffectData Data;
        public int OwnerID;
        public Func<EffectContext> ContextFactory;
        // targetID -> payload actuellement appliqué à cette cible par cette registration.
        public readonly Dictionary<int, object> Applied = new Dictionary<int, object>();
    }

    private static readonly List<Registration> _registrations = new List<Registration>();

    public static void Reset() => _registrations.Clear();

    public static void Register(CardEffectData data, int ownerID, Func<EffectContext> contextFactory)
    {
        if (data.Effect is not IPassiveAuraEffect)
        {
            Debug.LogWarning($"[PassiveAuraManager] '{data.EffectName}' ({data.Effect?.name}) est déclaré en Passive mais n'implémente pas IPassiveAuraEffect — ignoré.");
            return;
        }

        _registrations.Add(new Registration
        {
            Data = data,
            OwnerID = ownerID,
            ContextFactory = contextFactory,
        });

        // Pas seulement cette registration : une aura DÉJÀ active ailleurs peut désormais concerner
        // cette nouvelle entité (ex: "+1/+1 aux alliés dans ma zone" doit aussi buffer ce nouvel arrivant).
        RecomputeAll();
    }

    public static void Unregister(int ownerID)
    {
        List<Registration> toRemove = _registrations.Where(r => r.OwnerID == ownerID).ToList();
        foreach (Registration registration in toRemove)
        {
            RevertAllTargets(registration);
            _registrations.Remove(registration);
        }
        RecomputeAll();
    }

    // Planification de combat : ce que les cibles de la zone de `source` perdront quand elle mourra
    // réellement au rejeu (voir Recompute) — pour que ZoneCombatResolver.ReservePredictedAuraLoss
    // l'anticipe dès sa mort prédite, sans toucher aux stats live avant le rejeu. Vide si ces pertes
    // ont déjà été appliquées (source déjà IsPendingDeath : cibles déjà retirées de Applied).
    public static List<(CreatureLogic target, int attackLoss, int healthLoss)> GetStatAuraLossOnDeath(CreatureLogic source)
    {
        List<(CreatureLogic target, int attackLoss, int healthLoss)> result = new List<(CreatureLogic, int, int)>();
        if (source.IsPendingDeath) return result;

        ZoneLogic sourceZone = source.Zone;
        foreach (Registration registration in _registrations)
        {
            if (registration.OwnerID != source.UniqueCreatureID || registration.Data.Effect is not PassiveStatAuraSO) continue;
            foreach (KeyValuePair<int, object> applied in registration.Applied)
            {
                if (PhaseEffectPipeline.ResolveEntityByID(applied.Key) is not CreatureLogic target || target.Zone != sourceZone)
                    continue;
                (int attackBonus, int healthBonus) = ((int, int))applied.Value;
                result.Add((target, attackBonus, healthBonus));
            }
        }
        return result;
    }

    /// <summary>À appeler après tout changement de plateau (créature jouée/déplacée/morte, etc.).</summary>
    public static void RecomputeAll()
    {
        foreach (Registration registration in new List<Registration>(_registrations))
            Recompute(registration);
    }

    private static void Recompute(Registration registration)
    {
        IPassiveAuraEffect auraEffect = (IPassiveAuraEffect)registration.Data.Effect;
        EffectContext context = registration.ContextFactory();

        // Un combat se fait en deux temps : la planification prédit les morts (OnDeathResolvedInBattle,
        // pendingDamage) sans toucher aux PV, puis EnqueueBattleCommands rejoue les coups dans l'ordre sur
        // les PV live et marque chaque mort à son instant réel (MarkPendingDeath → IsPendingDeath →
        // RecomputeAll). Une aura ne bouge donc qu'au REJEU — la modifier en pleine planification ferait
        // rejouer des coups planifiés AVANT la mort de sa source sur des PV déjà réduits :
        //  - source morte (IsPendingDeath) : l'aura quitte tout de suite les unités de SA zone (dans une
        //    zone, ordre de rejeu = ordre de planification, et la planification l'a anticipé via
        //    ZoneCombatResolver.ReservePredictedAuraLoss), mais reste ailleurs jusqu'au drain : l'ordre de
        //    rejeu des zones (RoundOutcome.MainBaseOrder) n'est connu qu'après planification ;
        //  - cible morte (prédite ou réelle) encore sur le plateau : garde son buff jusqu'au drain.
        bool sourceDeadInBattle = context.Source != null && context.Source.IsPendingDeath;

        bool conditionMet = registration.Data.Condition == null || registration.Data.Condition.Evaluate(context);
        List<ILivable> currentTargets = conditionMet
            ? registration.Data.Effect.GetAffectedElements(context, registration.Data.Effectinfo).OfType<ILivable>().ToList()
            : new List<ILivable>();
        if (sourceDeadInBattle)
        {
            ZoneLogic sourceZone = context.Source.Zone;
            currentTargets = currentTargets.Where(t => t.Zone != sourceZone).ToList();
        }

        HashSet<int> currentIDs = new HashSet<int>(currentTargets.Select(t => t.ID));

        // Cibles qui ne sont plus concernées (sorties de zone, condition retombée à faux...).
        foreach (int staleID in registration.Applied.Keys.Where(id => !currentIDs.Contains(id)).ToList())
        {
            ILivable staleTarget = PhaseEffectPipeline.ResolveEntityByID(staleID) as ILivable;

            // Tuée en combat mais pas encore retirée du plateau : garde son buff jusqu'au drain (voir plus haut).
            if (staleTarget is CreatureLogic dying && !IsOffBoard(dying)
                && (dying.IsPendingDeath || dying.OnDeathResolvedInBattle))
                continue;

            if (staleTarget != null && !IsOffBoard(staleTarget))
                auraEffect.RevertAura(staleTarget, registration.Applied[staleID]);
            registration.Applied.Remove(staleID);
        }

        // Cibles concernées maintenant : applique, ou réapplique si le payload a changé (ex: aura scalée).
        foreach (ILivable target in currentTargets)
        {
            object newPayload = auraEffect.ComputeAuraPayload(context, target);
            registration.Applied.TryGetValue(target.ID, out object oldPayload);

            if (newPayload == null)
            {
                if (oldPayload != null)
                {
                    auraEffect.RevertAura(target, oldPayload);
                    registration.Applied.Remove(target.ID);
                }
                continue;
            }

            if (Equals(oldPayload, newPayload))
                continue;

            if (oldPayload != null && auraEffect is IUpdatablePassiveAura updatable)
                updatable.UpdateAura(target, oldPayload, newPayload);
            else
            {
                if (oldPayload != null)
                    auraEffect.RevertAura(target, oldPayload);
                auraEffect.ApplyAura(target, newPayload);
            }
            registration.Applied[target.ID] = newPayload;
        }
    }

    private static void RevertAllTargets(Registration registration)
    {
        IPassiveAuraEffect auraEffect = (IPassiveAuraEffect)registration.Data.Effect;
        foreach (KeyValuePair<int, object> applied in registration.Applied)
        {
            ILivable target = PhaseEffectPipeline.ResolveEntityByID(applied.Key) as ILivable;
            if (target != null && !IsOffBoard(target))
                auraEffect.RevertAura(target, applied.Value);
        }
        registration.Applied.Clear();
    }

    // Créature déjà retirée du plateau (morte, CreaturesCreatedThisGame n'étant jamais purgé) : on
    // oublie simplement son entrée sans toucher à ses stats — elles n'ont plus d'importance, et y
    // toucher relancerait Die() via le setter Health hors combat (PV déjà ramenés à 0 par Die()).
    private static bool IsOffBoard(ILivable target) =>
        target is CreatureLogic creature && !creature.owner.playedCards.Creatures.Contains(creature);
}
