using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Row Attack")]
public class RowAttackModifierSO : AttackModifierSO
{
    public override void Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
        foreach (CreatureLogic c in mainTarget.owner.playedCards.Creatures)
        {
            if (c == mainTarget) continue;
            if (c.BaseID != mainTarget.BaseID) continue;
            if (mainTarget.IsMelee && !c.IsMelee) continue;
            DealDamage(attacker, c);
        }
    }

    private void DealDamage(CreatureLogic attacker, CreatureLogic target)
    {
        if (target.IsPendingDeath) return;
        int dmg = attacker.Attack;
        int shieldAbs = Mathf.Min(dmg, target.ShieldValue);
        int effective = dmg - shieldAbs;
        int hpAfter = Mathf.Max(0, target.Health - effective);
        new DealDamageCommand(target.UniqueCreatureID, dmg, hpAfter, attacker.UniqueCreatureID, null, attacker.AttackSpeedMultiplier).AddToQueue();
        if (hpAfter <= 0)
            target.ScheduleBattleDeath();
        else
            target.Health -= effective;
    }
}
