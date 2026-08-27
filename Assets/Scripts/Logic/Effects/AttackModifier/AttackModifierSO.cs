using UnityEngine;
using System.Collections.Generic;

public abstract class AttackModifierSO : ScriptableObject
{
    // Mot-clé affiché quand ce modificateur est octroyé à l'exécution (via GrantAttackModifierSO).
    // Laisser vide si ce modificateur ne doit jamais apparaître comme mot-clé.
    public Keyword AssociatedKeyword;

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

    // Position X utilisée par les modificateurs cross-row (Cone/Circular/Ricochet/Piercing) pour
    // apparier une créature de mêlée avec la créature ranged la plus proche (et inversement).
    // Si la créature a déjà un GameObject, on lit sa position réelle (respecte un réordonnancement
    // manuel de la rangée par le joueur, qui n'affecte que le visuel — voir DragCreatureActions).
    // Sinon (créature créée à la volée pendant la planification d'un combat, dont l'apparition
    // visuelle est différée jusqu'à la mort de sa source — voir Command.DeferForBattleReplay /
    // CreatureLogic.ScheduleBattleDeath), on calcule la position qu'elle aura une fois révélée avec
    // la même formule que TableVisual.AddCreatureAtIndex (CenteredSlots.GetSlotPosition), pour
    // rester comparable aux positions réelles des autres créatures de la ligne.
    protected static float GetEffectiveWorldX(CreatureLogic creature)
    {
        GameObject go = IDHolder.GetGameObjectWithID(creature.UniqueCreatureID);
        if (go != null) return go.transform.position.x;

        PlayerArea area = creature.owner?.GetPlayerAreaByID(creature.BaseID);
        if (area?.tableVisual == null) return 0f;
        CenteredSlots rowSlots = creature.IsMelee && area.tableVisual.meleeSlots != null
            ? area.tableVisual.meleeSlots
            : area.tableVisual.rangedSlots;
        if (rowSlots == null) return 0f;

        List<CreatureLogic> row = new List<CreatureLogic>();
        foreach (CreatureLogic c in creature.owner.playedCards.Creatures)
            if (c.BaseID == creature.BaseID && c.IsMelee == creature.IsMelee)
                row.Add(c);
        int idx = row.IndexOf(creature);
        return idx < 0 ? 0f : rowSlots.GetSlotPosition(idx, row.Count).x;
    }
}
