using UnityEngine;
using System.Collections.Generic;

public abstract class AttackModifierSO : ScriptableObject
{
    // Résout, UNE SEULE FOIS pendant la planification serveur (ZoneCombatResolver.BuildAutoBattleSequence),
    // les cibles secondaires touchées par ce modificateur — sans jamais muter Health (la mutation réelle
    // se fait plus tard, en exécution, dans EnqueueBattleCommands, à partir du résultat figé ici).
    // isDead reflète l'état de mort SIMULÉ à cet instant précis de la marche séquentielle de planification
    // (ZoneCombatResolver.IsEffectivelyDead — dégâts cumulés dans pendingDamage vs Health+Shield), pas
    // l'état réel IsPendingDeath (toujours faux pendant la planification). C'est ce qui permet à un
    // modificateur de savoir qu'une cible a déjà été tuée par une attaque précédente dans la même passe
    // (principale ou secondaire d'un autre attaquant) et d'agir en conséquence (skip, retarget...).
    // Le résultat (ID de cible + dégât brut) est figé dans le BattleStepRecord correspondant et diffusé
    // tel quel à tous les clients — aucune décision de ciblage n'est plus recalculée à l'exécution.
    public abstract List<AttackHitResult> ResolveTargets(CreatureLogic attacker, CreatureLogic mainTarget, System.Func<CreatureLogic, bool> isDead);
}
