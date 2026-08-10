using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Lateral Attack")]
public class LateralAttackModifierSO : AttackModifierSO
{
    public override List<AttackHitResult> Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
        List<AttackHitResult> hits = new List<AttackHitResult>();
        List<CreatureLogic> adjacents = GetAdjacent(mainTarget);
        Debug.Log($"[LateralAttack] {attacker.DisplayName} → cible principale : {mainTarget.DisplayName} | voisins trouvés : {adjacents.Count}");

        foreach (CreatureLogic adj in adjacents)
        {
            if (adj.IsPendingDeath)
            {
                Debug.Log($"[LateralAttack] Voisin {adj.DisplayName} ignoré (PendingDeath)");
                continue;
            }
            int dmg = attacker.Attack;
            int shieldAbs = Mathf.Min(dmg, adj.ShieldValue);
            int effective = dmg - shieldAbs;
            int hpAfter = Mathf.Max(0, adj.Health - effective);
            Debug.Log($"[LateralAttack] Frappe {adj.DisplayName} — dégâts:{dmg} shield absorbé:{shieldAbs} effectif:{effective} HP avant:{adj.Health} HP après:{hpAfter}");
            hits.Add(new AttackHitResult(adj.UniqueCreatureID, dmg, hpAfter));
            // La mort (ScheduleBattleDeath) est mise en file par l'appelant (ZoneCombatResolver), après la
            // commande d'attaque principale — sinon CreatureDieCommand pourrait s'exécuter avant l'animation
            // d'attaque et la cible disparaîtrait avant même que le coup ne soit joué visuellement.
            if (hpAfter > 0)
                adj.Health -= effective;
        }

        return hits;
    }

    private List<CreatureLogic> GetAdjacent(CreatureLogic target)
    {
        List<CreatureLogic> sameRow = new List<CreatureLogic>();
        foreach (CreatureLogic c in target.owner.playedCards.Creatures)
            if (c.BaseID == target.BaseID && c.IsMelee == target.IsMelee)
                sameRow.Add(c);
        int idx = sameRow.IndexOf(target);
        Debug.Log($"[LateralAttack] GetAdjacent — rangée de {target.DisplayName} : [{string.Join(", ", sameRow.ConvertAll(c => c.DisplayName))}] | idx:{idx}");
        List<CreatureLogic> result = new List<CreatureLogic>();
        if (idx > 0) result.Add(sameRow[idx - 1]);
        if (idx >= 0 && idx < sameRow.Count - 1) result.Add(sameRow[idx + 1]);
        return result;
    }
}
