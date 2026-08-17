using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Piercing Attack")]
public class PiercingAttackModifierSO : AttackModifierSO
{
    public override List<AttackHitResult> ResolveTargets(CreatureLogic attacker, CreatureLogic mainTarget, System.Func<CreatureLogic, bool> isDead)
    {
        List<AttackHitResult> hits = new List<AttackHitResult>();
        CreatureLogic pierced = GetPiercedTarget(mainTarget, isDead);
        if (pierced == null) return hits;

        int dmg = attacker.Attack;
        int shieldAbs = Mathf.Min(dmg, pierced.ShieldValue);
        int effective = dmg - shieldAbs;
        int hpAfter = Mathf.Max(0, pierced.Health - effective);
        hits.Add(new AttackHitResult(pierced.UniqueCreatureID, dmg, hpAfter));
        return hits;
    }

    // Cible la créature ranged la plus proche (par position) qui n'est pas déjà morte à ce point de la
    // résolution — au lieu de toujours prendre la plus proche tout court. Sans ça, si la créature la plus
    // proche a déjà été tuée par un Piercing précédent dans la même passe, l'attaque ne fait rien du tout
    // au lieu de percer jusqu'à la prochaine créature vivante.
    private CreatureLogic GetPiercedTarget(CreatureLogic mainTarget, System.Func<CreatureLogic, bool> isDead)
    {
        if (!mainTarget.IsMelee)
            return null;

        List<CreatureLogic> rangedRow = GetRow(mainTarget, false);
        return FindClosestAliveCrossRow(mainTarget, rangedRow, isDead);
    }

    private CreatureLogic FindClosestAliveCrossRow(CreatureLogic from, List<CreatureLogic> targetRow, System.Func<CreatureLogic, bool> isDead)
    {
        float fromX = GetEffectiveWorldX(from);

        CreatureLogic closest = null;
        float minDist = float.MaxValue;
        foreach (CreatureLogic c in targetRow)
        {
            if (isDead(c)) continue;
            float dist = Mathf.Abs(GetEffectiveWorldX(c) - fromX);
            if (dist < minDist) { minDist = dist; closest = c; }
        }
        return closest;
    }

    private List<CreatureLogic> GetRow(CreatureLogic reference, bool melee)
    {
        List<CreatureLogic> row = new List<CreatureLogic>();
        foreach (CreatureLogic c in reference.owner.playedCards.Creatures)
            if (c.BaseID == reference.BaseID && c.IsMelee == melee)
                row.Add(c);
        return row;
    }
}
