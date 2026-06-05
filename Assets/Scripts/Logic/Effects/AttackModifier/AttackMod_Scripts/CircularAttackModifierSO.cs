using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Circular Attack")]
public class CircularAttackModifierSO : AttackModifierSO
{
    public override void Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
        List<CreatureLogic> sameRow = GetRow(mainTarget, mainTarget.IsMelee);
        int idx = sameRow.IndexOf(mainTarget);
        if (idx < 0) return;

        if (idx > 0)                        
            DealDamage(attacker, sameRow[idx - 1]);
        if (idx < sameRow.Count - 1)       
            DealDamage(attacker, sameRow[idx + 1]);

        if (mainTarget.IsMelee)
        {
            List<CreatureLogic> rangedRow = GetRow(mainTarget, false);
            int rangedIdx = FindCrossRowIdx(mainTarget, rangedRow);
            if (rangedIdx >= 0)
                for (int i = rangedIdx - 1; i <= rangedIdx + 1; i++)
                {
                    if (i < 0 || i >= rangedRow.Count) continue;
                    DealDamage(attacker, rangedRow[i]);
                }
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
