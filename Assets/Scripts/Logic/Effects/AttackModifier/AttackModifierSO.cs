using UnityEngine;
using System.Collections.Generic;

public abstract class AttackModifierSO : ScriptableObject
{
    // Retourne les cibles secondaires touchées (dégâts déjà appliqués en logique) afin que
    // l'appelant (ZoneCombatResolver) les regroupe avec la cible principale dans une seule
    // commande visuelle — un seul windup, un tir par cible, un seul retour.
    public abstract List<AttackHitResult> Apply(CreatureLogic attacker, CreatureLogic mainTarget);
}
