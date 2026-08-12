using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Row Attack")]
public class RowAttackModifierSO : AttackModifierSO
{
    public override List<AttackHitResult> Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
        List<AttackHitResult> hits = new List<AttackHitResult>();
        foreach (CreatureLogic c in mainTarget.owner.playedCards.Creatures)
        {
            if (c == mainTarget) continue;
            if (c.BaseID != mainTarget.BaseID) continue;
            if (mainTarget.IsMelee && !c.IsMelee) continue;
            TryDealDamage(attacker, c, hits);
        }
        return hits;
    }

    private void TryDealDamage(CreatureLogic attacker, CreatureLogic target, List<AttackHitResult> hits)
    {
        if (target.IsPendingDeath) return;
        int dmg = attacker.Attack;
        int shieldAbs = Mathf.Min(dmg, target.ShieldValue);
        int effective = dmg - shieldAbs;
        int hpAfter = Mathf.Max(0, target.Health - effective);
        hits.Add(new AttackHitResult(target.UniqueCreatureID, dmg, hpAfter));
        // La mort (ScheduleBattleDeath) est mise en file par l'appelant (ZoneCombatResolver), après la
        // commande d'attaque principale — sinon CreatureDieCommand pourrait s'exécuter avant l'animation
        // d'attaque et la cible disparaîtrait avant même que le coup ne soit joué visuellement.
        if (hpAfter > 0)
            target.Health -= effective;
    }
}
