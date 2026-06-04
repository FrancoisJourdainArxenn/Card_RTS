using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Piercing Attack")]
public class PiercingAttackModifierSO : AttackModifierSO
{
    public override void Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
        if (!mainTarget.IsMelee)
            return;

        List<CreatureLogic> rangedRow = GetRow(mainTarget, false);
        int idx = FindCrossRowIdx(mainTarget, rangedRow);
        if (idx < 0)
            return;

        CreatureLogic pierced = rangedRow[idx];
        if (pierced.IsPendingDeath)
            return;

        int dmg = attacker.Attack;
        int shieldAbs = Mathf.Min(dmg, pierced.ShieldValue);
        int effective = dmg - shieldAbs;
        int hpAfter = Mathf.Max(0, pierced.Health - effective);

        new DealDamageCommand(pierced.UniqueCreatureID, dmg, hpAfter, attacker.UniqueCreatureID, null, attacker.AttackSpeedMultiplier).AddToQueue();
        if (hpAfter <= 0)
            pierced.ScheduleBattleDeath();
        else
            pierced.Health -= effective;
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
