using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Ricochet Attack")]
public class RicochetAttackModifierSO : AttackModifierSO
{
    public int Bounces = 3;

    public override List<AttackHitResult> ResolveTargets(CreatureLogic attacker, CreatureLogic mainTarget, System.Func<CreatureLogic, bool> isDead)
    {
        List<AttackHitResult> hits = new List<AttackHitResult>();
        foreach (CreatureLogic target in ComputeBouncePath(attacker, mainTarget, isDead))
            TryAddHit(attacker, target, hits, isDead);
        return hits;
    }

    private List<CreatureLogic> ComputeBouncePath(CreatureLogic attacker, CreatureLogic mainTarget, System.Func<CreatureLogic, bool> isDead)
    {
        List<CreatureLogic> path = new List<CreatureLogic>();
        HashSet<int> hit = new HashSet<int>();
        hit.Add(mainTarget.UniqueCreatureID);

        CreatureLogic current = mainTarget;
        for (int i = 0; i < Bounces; i++)
        {
            List<CreatureLogic> candidates = GetAdjacentCandidates(current, hit, isDead);
            if (candidates.Count == 0) break;

            // Seed déterministe (au lieu de Random.Range) : la résolution ne tourne plus qu'une seule fois,
            // côté serveur, pendant la planification — mais garder un choix reproductible reste utile pour
            // le debug/replay et ne coûte rien.
            int seed = attacker.UniqueCreatureID ^ current.UniqueCreatureID ^ (i * 1000);
            CreatureLogic next = candidates[Mathf.Abs(seed) % candidates.Count];
            hit.Add(next.UniqueCreatureID);
            path.Add(next);
            current = next;
        }

        return path;
    }

    private List<CreatureLogic> GetAdjacentCandidates(CreatureLogic from, HashSet<int> alreadyHit, System.Func<CreatureLogic, bool> isDead)
    {
        List<CreatureLogic> sameRow = GetRow(from, from.IsMelee);
        int idx = sameRow.IndexOf(from);
        if (idx < 0) return new List<CreatureLogic>();

        List<CreatureLogic> candidates = new List<CreatureLogic>();

        if (idx > 0)                    TryAdd(candidates, sameRow[idx - 1], alreadyHit, isDead);
        if (idx < sameRow.Count - 1)   TryAdd(candidates, sameRow[idx + 1], alreadyHit, isDead);

        List<CreatureLogic> otherRow = GetRow(from, !from.IsMelee);
        int otherIdx = FindCrossRowIdx(from, otherRow);
        if (otherIdx >= 0)
            for (int i = otherIdx - 1; i <= otherIdx + 1; i++)
            {
                if (i < 0 || i >= otherRow.Count) continue;
                TryAdd(candidates, otherRow[i], alreadyHit, isDead);
            }

        return candidates;
    }

    private void TryAdd(List<CreatureLogic> list, CreatureLogic c, HashSet<int> alreadyHit, System.Func<CreatureLogic, bool> isDead)
    {
        if (!isDead(c) && !alreadyHit.Contains(c.UniqueCreatureID))
            list.Add(c);
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
