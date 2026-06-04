using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Attack Modifiers/Ricochet Attack")]
public class RicochetAttackModifierSO : AttackModifierSO
{
    public int Bounces = 3;

    public override void Apply(CreatureLogic attacker, CreatureLogic mainTarget)
    {
        HashSet<int> hit = new HashSet<int>();
        hit.Add(mainTarget.UniqueCreatureID);

        CreatureLogic current = mainTarget;
        for (int i = 0; i < Bounces; i++)
        {
            List<CreatureLogic> candidates = GetAdjacentCandidates(current, hit);
            if (candidates.Count == 0) break;

            // Seed déterministe pour éviter la désync réseau : Random.Range indépendant sur chaque client
            // produirait des chemins différents. La seed garantit le même résultat des deux côtés.
            // Dépendance résiduelle : FindCrossRowIdx lit des transform.position, ce qui est safe tant
            // qu'aucune animation ne démarre avant la fin de EnqueueBattleCommands (c'est le cas aujourd'hui).
            // Solution robuste future : pré-calculer le chemin complet côté serveur dans BuildAutoBattleSequence
            // et le sérialiser comme BattleStepRecords supplémentaires, évitant toute dépendance au visuel.
            int seed = attacker.UniqueCreatureID ^ current.UniqueCreatureID ^ (i * 1000);
            CreatureLogic next = candidates[Mathf.Abs(seed) % candidates.Count];
            hit.Add(next.UniqueCreatureID);
            DealDamage(attacker, next);
            current = next;
        }
    }

    private List<CreatureLogic> GetAdjacentCandidates(CreatureLogic from, HashSet<int> alreadyHit)
    {
        List<CreatureLogic> sameRow = GetRow(from, from.IsMelee);
        int idx = sameRow.IndexOf(from);
        if (idx < 0) return new List<CreatureLogic>();

        List<CreatureLogic> candidates = new List<CreatureLogic>();

        if (idx > 0)                    TryAdd(candidates, sameRow[idx - 1], alreadyHit);
        if (idx < sameRow.Count - 1)   TryAdd(candidates, sameRow[idx + 1], alreadyHit);

        List<CreatureLogic> otherRow = GetRow(from, !from.IsMelee);
        int otherIdx = FindCrossRowIdx(from, otherRow);
        if (otherIdx >= 0)
            for (int i = otherIdx - 1; i <= otherIdx + 1; i++)
            {
                if (i < 0 || i >= otherRow.Count) continue;
                TryAdd(candidates, otherRow[i], alreadyHit);
            }

        return candidates;
    }

    private void TryAdd(List<CreatureLogic> list, CreatureLogic c, HashSet<int> alreadyHit)
    {
        if (!c.IsPendingDeath && !alreadyHit.Contains(c.UniqueCreatureID))
            list.Add(c);
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
