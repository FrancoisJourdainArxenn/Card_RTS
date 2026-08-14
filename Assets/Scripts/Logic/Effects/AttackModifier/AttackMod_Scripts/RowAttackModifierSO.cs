using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Row Attack")]
public class RowAttackModifierSO : AttackModifierSO
{
    public override List<AttackHitResult> ResolveTargets(CreatureLogic attacker, CreatureLogic mainTarget, System.Func<CreatureLogic, bool> isDead)
    {
        List<AttackHitResult> hits = new List<AttackHitResult>();
        foreach (CreatureLogic t in GetRowTargets(mainTarget))
            TryAddHit(attacker, t, hits, isDead);
        return hits;
    }

    private List<CreatureLogic> GetRowTargets(CreatureLogic mainTarget)
    {
        List<CreatureLogic> targets = new List<CreatureLogic>();
        foreach (CreatureLogic c in mainTarget.owner.playedCards.Creatures)
        {
            if (c == mainTarget) continue;
            if (c.BaseID != mainTarget.BaseID) continue;
            if (mainTarget.IsMelee && !c.IsMelee) continue;
            targets.Add(c);
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
}
