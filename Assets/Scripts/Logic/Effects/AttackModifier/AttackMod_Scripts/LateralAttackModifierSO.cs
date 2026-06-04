using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Lateral Attack")]
public class LateralAttackModifierSO : AttackModifierSO
{
    public override void Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
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
            new DealDamageCommand(adj.UniqueCreatureID, dmg, hpAfter, attacker.UniqueCreatureID, null, attacker.AttackSpeedMultiplier).AddToQueue();
            if (hpAfter <= 0)
                adj.ScheduleBattleDeath();
            else
                adj.Health -= effective;
        }
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
