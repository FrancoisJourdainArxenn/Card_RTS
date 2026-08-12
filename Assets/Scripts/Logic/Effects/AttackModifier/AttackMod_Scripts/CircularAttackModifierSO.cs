using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Circular Attack")]
public class CircularAttackModifierSO : AttackModifierSO
{
    public override List<AttackHitResult> Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
        List<AttackHitResult> hits = new List<AttackHitResult>();
        List<CreatureLogic> sameRow = GetRow(mainTarget, mainTarget.IsMelee);
        int idx = sameRow.IndexOf(mainTarget);
        if (idx < 0) return hits;

        if (idx > 0)
            TryDealDamage(attacker, sameRow[idx - 1], hits);
        if (idx < sameRow.Count - 1)
            TryDealDamage(attacker, sameRow[idx + 1], hits);

        if (mainTarget.IsMelee)
        {
            List<CreatureLogic> rangedRow = GetRow(mainTarget, false);
            int rangedIdx = FindCrossRowIdx(mainTarget, rangedRow);
            if (rangedIdx >= 0)
                for (int i = rangedIdx - 1; i <= rangedIdx + 1; i++)
                {
                    if (i < 0 || i >= rangedRow.Count) continue;
                    TryDealDamage(attacker, rangedRow[i], hits);
                }
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

    private int FindCrossRowIdx(CreatureLogic from, List<CreatureLogic> targetRow)
    {
        GameObject fromGO = IDHolder.GetGameObjectWithID(from.UniqueCreatureID);
        if (fromGO == null || targetRow.Count == 0) return -1;
        float fromX = fromGO.transform.position.x;
        int closest = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < targetRow.Count; i++)
        {
            GameObject go = IDHolder.GetGameObjectWithID(targetRow[i].UniqueCreatureID);
            if (go == null) continue;
            float dist = Mathf.Abs(go.transform.position.x - fromX);
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
