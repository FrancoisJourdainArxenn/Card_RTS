// Contrat pour qu'un EffectSO soit utilisable avec TriggerType.Passive.
// IMPORTANT : ces 3 méthodes ne doivent RIEN lire/écrire sur des champs d'instance du ScriptableObject
// (contrairement à Execute()/ApplyToTarget()) — le même asset peut être actif simultanément sur
// plusieurs sources (plusieurs créatures utilisant la même carte). PassiveAuraManager garde lui-même
// la trace de ce qu'il a appliqué à qui, jamais l'EffectSO.
public interface IPassiveAuraEffect
{
    // Fonction pure de (context, target) : ce que CETTE cible doit recevoir maintenant.
    // null = rien à accorder à cette cible (rare, le ciblage/Condition ont déjà filtré l'essentiel).
    object ComputeAuraPayload(EffectContext context, ILivable target);

    // Applique à `target` exactement ce que ComputeAuraPayload a produit.
    void ApplyAura(ILivable target, object payload);

    // Retire de `target` exactement ce que ApplyAura(target, payload) avait accordé.
    void RevertAura(ILivable target, object payload);
}

// Aura dont le payload peut changer en cours de vie (ex: PassiveStatAuraSO qui grandit chaque tour) :
// PassiveAuraManager passe directement de l'ancien au nouveau payload au lieu de Revert puis Apply —
// sinon une aura de Vie qui grandit retirerait d'abord ses PV à une créature blessée (mort à 0 hors
// combat) et afficherait deux pops au lieu d'un.
public interface IUpdatablePassiveAura
{
    void UpdateAura(ILivable target, object oldPayload, object newPayload);
}
