using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Circular Attack")]
public class CircularAttackModifierSO : AttackModifierSO
{
    public override List<AttackHitResult> ResolveTargets(CreatureLogic attacker, CreatureLogic mainTarget, System.Func<CreatureLogic, bool> isDead)
    {
        List<AttackHitResult> hits = new List<AttackHitResult>();
        foreach (CreatureLogic t in GetCircularTargets(mainTarget))
            TryAddHit(attacker, t, hits, isDead);
        return hits;
    }

    private List<CreatureLogic> GetCircularTargets(CreatureLogic mainTarget)
    {
        List<CreatureLogic> targets = new List<CreatureLogic>();
        List<CreatureLogic> sameRow = GetRow(mainTarget, mainTarget.IsMelee);
        int idx = sameRow.IndexOf(mainTarget);
        if (idx < 0) return targets;

        if (idx > 0)
            targets.Add(sameRow[idx - 1]);
        if (idx < sameRow.Count - 1)
            targets.Add(sameRow[idx + 1]);

        if (mainTarget.IsMelee)
        {
            List<CreatureLogic> rangedRow = GetRow(mainTarget, false);
            int rangedIdx = FindCrossRowIdx(mainTarget, rangedRow);
            if (rangedIdx >= 0)
                for (int i = rangedIdx - 1; i <= rangedIdx + 1; i++)
                {
                    if (i < 0 || i >= rangedRow.Count) continue;
                    targets.Add(rangedRow[i]);
                }
        }

        return targets;
    }

    private void TryAddHit(CreatureLogic attacker, CreatureLogic target, List<AttackHitResult> hits, System.Func<CreatureLogic, bool> isDead)
    {
        if (isDead(target)) return;
        int dmg = attacker.Attack;
        int shieldAbs = Mathf.Min(dmg, target.ShieldValue);
        int effective = dmg - shieldAbs;
        int hpAfter = Mathf.Max(0, target.Health - effective);
        hits.Add(new AttackHitResult(target.UniqueCreatureID, dmg, hpAfter));
    }

    private int FindCrossRowIdx(CreatureLogic from, List<CreatureLogic> targetRow)
    {
        if (targetRow.Count == 0) return -1;
        float fromX = GetEffectiveWorldX(from);
        int closest = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < targetRow.Count; i++)
        {
            float dist = Mathf.Abs(GetEffectiveWorldX(targetRow[i]) - fromX);
            if (dist < minDist) { minDist = dist; closest = i; }
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
