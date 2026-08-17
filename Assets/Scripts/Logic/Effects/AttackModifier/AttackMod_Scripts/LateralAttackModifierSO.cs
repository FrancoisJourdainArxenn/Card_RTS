using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Lateral Attack")]
public class LateralAttackModifierSO : AttackModifierSO
{
    public override List<AttackHitResult> ResolveTargets(CreatureLogic attacker, CreatureLogic mainTarget, System.Func<CreatureLogic, bool> isDead)
    {
        List<CreatureLogic> adjacents = GetAdjacent(mainTarget);
        Debug.Log($"[Resolve][LateralAttack] {attacker.DisplayName} → cible principale : {mainTarget.DisplayName} | voisins trouvés : {adjacents.Count}");

        List<AttackHitResult> hits = new List<AttackHitResult>();
        foreach (CreatureLogic adj in adjacents)
        {
            if (isDead(adj))
            {
                Debug.Log($"[Resolve][LateralAttack] Voisin {adj.DisplayName} ignoré (déjà mort à ce point de la résolution)");
                continue;
            }
            int dmg = attacker.Attack;
            int shieldAbs = Mathf.Min(dmg, adj.ShieldValue);
            int effective = dmg - shieldAbs;
            int hpAfter = Mathf.Max(0, adj.Health - effective);
            hits.Add(new AttackHitResult(adj.UniqueCreatureID, dmg, hpAfter));
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
        List<CreatureLogic> result = new List<CreatureLogic>();
        if (idx > 0) result.Add(sameRow[idx - 1]);
        if (idx >= 0 && idx < sameRow.Count - 1) result.Add(sameRow[idx + 1]);
        return result;
    }
}
