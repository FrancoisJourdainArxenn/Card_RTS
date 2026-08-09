using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Cone Attack")]
public class ConeAttackModifierSO : AttackModifierSO
{
    public override List<AttackHitResult> Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
        List<AttackHitResult> hits = new List<AttackHitResult>();
        if (!mainTarget.IsMelee)
            return hits;

        List<CreatureLogic> rangedRow = GetRow(mainTarget, false);
        int idx = FindCrossRowIdx(mainTarget, rangedRow);
        if (idx < 0)
            return hits;

        TryDealDamage(attacker, rangedRow[idx], hits);

        if (idx > 0)                        TryDealDamage(attacker, rangedRow[idx - 1], hits);
        if (idx < rangedRow.Count - 1)      TryDealDamage(attacker, rangedRow[idx + 1], hits);

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
        if (hpAfter <= 0)
            target.ScheduleBattleDeath();
        else
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
