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

        // Le combat résout la mort d'une créature immédiatement, en pleine planification (voir
        // CreatureLogic.ResolvePredictedBattleDeath, appelé par ZoneCombatResolver dès que la créature
        // franchit le seuil de mort) — bien avant le vrai Die()/UnregisterEntity, qui n'arrive qu'au
        // drain de fin de Battle. Une source déjà tuée prédictivement ne doit plus accorder son aura
        // pour le reste de CE combat, même si elle est encore techniquement enregistrée ici.
        bool sourceAlive = context.Source == null || (!context.Source.IsPendingDeath && !context.Source.OnDeathResolvedInBattle);

        bool conditionMet = sourceAlive
            && (registration.Data.Condition == null || registration.Data.Condition.Evaluate(context));
        List<ILivable> currentTargets = conditionMet
            ? registration.Data.Effect.GetAffectedElements(context, registration.Data.Effectinfo).OfType<ILivable>().ToList()
            : new List<ILivable>();

        HashSet<int> currentIDs = new HashSet<int>(currentTargets.Select(t => t.ID));

        // Cibles qui ne sont plus concernées (sorties de zone, condition retombée à faux...).
        foreach (int staleID in registration.Applied.Keys.Where(id => !currentIDs.Contains(id)).ToList())
        {
            ILivable staleTarget = PhaseEffectPipeline.ResolveEntityByID(staleID) as ILivable;
            if (staleTarget != null)
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

            if (oldPayload != null)
                auraEffect.RevertAura(target, oldPayload);
            auraEffect.ApplyAura(target, newPayload);
            registration.Applied[target.ID] = newPayload;
        }
    }

    private static void RevertAllTargets(Registration registration)
    {
        IPassiveAuraEffect auraEffect = (IPassiveAuraEffect)registration.Data.Effect;
        foreach (KeyValuePair<int, object> applied in registration.Applied)
        {
            ILivable target = PhaseEffectPipeline.ResolveEntityByID(applied.Key) as ILivable;
            if (target != null)
                auraEffect.RevertAura(target, applied.Value);
        }
        registration.Applied.Clear();
    }
}
