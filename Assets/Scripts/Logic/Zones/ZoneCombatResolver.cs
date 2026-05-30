using System.Collections.Generic;
using UnityEngine;

public class ZoneCombatResolver : MonoBehaviour
{
    private Dictionary<int, int> pendingDamage = new Dictionary<int, int>();
    private static List<ZoneCombatResolver> allResolvers = new List<ZoneCombatResolver>();
    public static IReadOnlyList<ZoneCombatResolver> AllResolvers => allResolvers;
    private Dictionary<int, int> pendingBaseDamage = new Dictionary<int, int>();
    private Dictionary<int, int> pendingPlayerDamage   = new Dictionary<int, int>();
    private Dictionary<int, int> pendingBuildingDamage = new Dictionary<int, int>();

    private ZoneManager zoneView;
    private int p1FreePool;
    private int p2FreePool;
    private List<BattleStepRecord> _lastBattleSteps;

    private enum TargetKind { Creature, Building, Base, Player }
    private struct BattleStepRecord
    {
        public int attackerID;
        public bool attackerIsBuilding;
        public int targetID;
        public TargetKind targetKind;
        public int damage;
        public int targetOwnerPlayerID;
    }

    void Awake()
    {
        zoneView = GetComponent<ZoneManager>();
        allResolvers.Add(this);
    }

    public bool HasPossibleCombat()
    {
        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;
        bool p1HasBuildingAtk = GetBuildingsInMyZone(p1, zoneView).Count > 0;
        bool p2HasBuildingAtk = GetBuildingsInMyZone(p2, zoneView).Count > 0;
        int p1CreatureCount = GetCreaturesInMyZone(p1, zoneView).Count;
        int p2CreatureCount = GetCreaturesInMyZone(p2, zoneView).Count;

        if (
            (p1CreatureCount > 0 && p2CreatureCount > 0) ||
            (FindDefenderBaseInZone(p1) != null && p2CreatureCount > 0) ||
            (FindDefenderBaseInZone(p2) != null && p1CreatureCount > 0) ||
            (zoneView.subZones.Contains(p1.MainPArea) && p2CreatureCount > 0) ||
            (zoneView.subZones.Contains(p2.MainPArea) && p1CreatureCount > 0) ||
            (p1HasBuildingAtk && p2CreatureCount > 0) ||
            (p2HasBuildingAtk && p1CreatureCount > 0) ||
            (p1CreatureCount > 0 && GetAllBuildingsInMyZone(p2, zoneView).Count > 0) ||
            (p2CreatureCount > 0 && GetAllBuildingsInMyZone(p1, zoneView).Count > 0) ||
            (p1HasBuildingAtk && FindDefenderBaseInZone(p2) != null) ||
            (p2HasBuildingAtk && FindDefenderBaseInZone(p1) != null) ||
            (p1HasBuildingAtk && zoneView.subZones.Contains(p2.MainPArea)) ||
            (p2HasBuildingAtk && zoneView.subZones.Contains(p1.MainPArea))
        )
            return true;
        return false;
    }

    public void OnBattlePhaseStart()
    {
        pendingDamage.Clear();
        pendingBaseDamage.Clear();
        pendingPlayerDamage.Clear();
        pendingBuildingDamage.Clear();
        p1FreePool = 0;
        p2FreePool = 0;

        if (NetworkSessionData.IsNetworkSession)
        {
            if (Unity.Netcode.NetworkManager.Singleton.IsServer)
                _lastBattleSteps = BuildAutoBattleSequence(zoneView);
        }
        else
        {
            List<BattleStepRecord> steps = BuildAutoBattleSequence(zoneView);
            pendingDamage.Clear();
            pendingBaseDamage.Clear();
            pendingPlayerDamage.Clear();
            pendingBuildingDamage.Clear();
            EnqueueBattleCommands(steps);
        }
        RefreshAllAreaStats();
    }

    public void OnBattlePhaseEnd()
    {
        foreach (PlayerArea pa in zoneView.subZones)
            if (pa.tableVisual != null)
                new RefreshTableSlotsCommand(pa.tableVisual).AddToQueue();

        pendingDamage.Clear();
        ClearAllIndicators();
    }

    PlayerArea FindAreaForCreature(CreatureLogic creature)
    {
        foreach (PlayerArea pa in zoneView.subZones)
            if (pa.baseID == creature.BaseID)
                return pa;
        return null;
    }

    List<BattleStepRecord> BuildAutoBattleSequence(ZoneManager zone)
    {
        List<BattleStepRecord> steps = new();
        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;

        List<(int attack, bool isBuilding, int id)> queue1 = BuildAttackQueue(p1, zone);
        List<(int attack, bool isBuilding, int id)> queue2 = BuildAttackQueue(p2, zone);

        bool p1Turn = UnityEngine.Random.value < 0.5f;
        Debug.Log($"[Sequence] Commence : {(p1Turn ? p1.name : p2.name)}");
        int i1 = 0, i2 = 0, stepNum = 0;

        while (true)
        {
            while (i1 < queue1.Count && IsAttackerDead(queue1[i1]))
            { Debug.Log($"  [Skip mort] {p1.name} — ID:{queue1[i1].id}"); i1++; }
            while (i2 < queue2.Count && IsAttackerDead(queue2[i2]))
            { Debug.Log($"  [Skip mort] {p2.name} — ID:{queue2[i2].id}"); i2++; }

            bool p1CanAct = i1 < queue1.Count;
            bool p2CanAct = i2 < queue2.Count;

            if (!p1CanAct && !p2CanAct) break;

            if (p1CanAct && (!p2CanAct || p1Turn))
            {
                (int attack, bool isBuilding, int id) attacker = queue1[i1++];
                // Debug.Log($"[Step {stepNum++}] {p1.name} — ID:{attacker.id} atk:{attacker.attack}");
                (int overflow, BattleStepRecord? step) = AssignSingleAttack(attacker, p2, zone);
                p1FreePool += overflow;
                if (step.HasValue) steps.Add(step.Value);
            }
            else
            {
                (int attack, bool isBuilding, int id) attacker = queue2[i2++];
                // Debug.Log($"[Step {stepNum++}] {p2.name} — ID:{attacker.id} atk:{attacker.attack}");
                (int overflow, BattleStepRecord? step) = AssignSingleAttack(attacker, p1, zone);
                p2FreePool += overflow;
                if (step.HasValue) steps.Add(step.Value);
            }

            if (p1CanAct && p2CanAct) p1Turn = !p1Turn;
            else p1Turn = p1CanAct;
        }
        Debug.Log($"[Sequence] {steps.Count} step(s) générés au total");
        return steps;
    }

    // Ordre : mêlée créatures → non-mêlée créatures → mêlée bâtiments → non-mêlée bâtiments
    List<(int attack, bool isBuilding, int id)> BuildAttackQueue(Player player, ZoneManager zone)
    {
        List<(int, bool, int)> result = new();
        List<CreatureLogic> creatures = GetCreaturesInMyZone(player, zone);

        foreach (CreatureLogic c in creatures)
            if (c.IsMelee && c.Attack > 0) result.Add((c.Attack, false, c.UniqueCreatureID));
        foreach (CreatureLogic c in creatures)
            if (!c.IsMelee && c.Attack > 0) result.Add((c.Attack, false, c.UniqueCreatureID));

        List<BuildingLogic> buildings = GetBuildingsInMyZone(player, zone);
        foreach (BuildingLogic b in buildings)
            if (b.IsMelee) result.Add((b.Attack, true, b.UniqueBuildingID));
        foreach (BuildingLogic b in buildings)
            if (!b.IsMelee) result.Add((b.Attack, true, b.UniqueBuildingID));

        foreach ((int atk, bool isBuilding, int id) in result)
        {
            string name = isBuilding
                ? (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(id, out BuildingLogic bl) ? bl.DisplayName : id.ToString())
                : (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic cl) ? cl.DisplayName : id.ToString());
        }


        return result;
    }

    // Retourne le surplus de dégâts non placés (overflow) et la description de l'attaque pour animation
    // Priorité : mêlée bâtiment → mêlée créature → ranged créature → ranged bâtiment → base → joueur
    (int, BattleStepRecord?) AssignSingleAttack((int attack, bool isBuilding, int id) attacker, Player defender, ZoneManager zone)
    {
        int dmg = attacker.attack;
        List<CreatureLogic> creatures = GetCreaturesInMyZone(defender, zone);
        List<BuildingLogic> buildings = GetAllBuildingsInMyZone(defender, zone);

        // Tier 1 : bâtiments mêlée
        List<BuildingLogic> eligibleMeleeBuildings = new List<BuildingLogic>();
        foreach (BuildingLogic b in buildings)
        {
            if (!b.IsMelee || IsEffectivelyDeadBuilding(b)) continue;
            eligibleMeleeBuildings.Add(b);
        }
        if (eligibleMeleeBuildings.Count > 0)
        {
            BuildingLogic b = eligibleMeleeBuildings[UnityEngine.Random.Range(0, eligibleMeleeBuildings.Count)];
            pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int existing);
            int assign = Mathf.Min(dmg, b.Health - existing);
            pendingBuildingDamage[b.UniqueBuildingID] = existing + assign;
            if (b.Attack > 0)
            {
                if (!attacker.isBuilding)
                {
                    pendingDamage.TryGetValue(attacker.id, out int attackerExisting);
                    pendingDamage[attacker.id] = attackerExisting + b.Attack;
                }
                else
                {
                    pendingBuildingDamage.TryGetValue(attacker.id, out int attackerExisting);
                    pendingBuildingDamage[attacker.id] = attackerExisting + b.Attack;
                }
            }
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = b.UniqueBuildingID, targetKind = TargetKind.Building, damage = assign, targetOwnerPlayerID = defender.PlayerID });
        }

        // Tier 2 : créatures mêlée
        List<CreatureLogic> eligibleMeleeCreatures = new List<CreatureLogic>();
        foreach (CreatureLogic t in creatures)
        {
            if (!t.IsMelee || IsEffectivelyDead(t)) continue;
            eligibleMeleeCreatures.Add(t);
        }
        if (eligibleMeleeCreatures.Count > 0)
        {
            CreatureLogic t = eligibleMeleeCreatures[UnityEngine.Random.Range(0, eligibleMeleeCreatures.Count)];
            pendingDamage.TryGetValue(t.UniqueCreatureID, out int existing);
            int assign = Mathf.Min(dmg, t.Health + t.ShieldValue - existing);
            pendingDamage[t.UniqueCreatureID] = existing + assign;
            if (!attacker.isBuilding)
            {
                pendingDamage.TryGetValue(attacker.id, out int attackerExisting);
                pendingDamage[attacker.id] = attackerExisting + t.Attack;
            }
            else
            {
                pendingBuildingDamage.TryGetValue(attacker.id, out int attackerExisting);
                pendingBuildingDamage[attacker.id] = attackerExisting + t.Attack;
            }
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = t.UniqueCreatureID, targetKind = TargetKind.Creature, damage = assign, targetOwnerPlayerID = defender.PlayerID });
        }

        // Tier 3 : créatures ranged
        List<CreatureLogic> eligibleRangedCreatures = new List<CreatureLogic>();
        foreach (CreatureLogic t in creatures)
        {
            if (t.IsMelee || IsEffectivelyDead(t)) continue;
            eligibleRangedCreatures.Add(t);
        }
        if (eligibleRangedCreatures.Count > 0)
        {
            CreatureLogic t = eligibleRangedCreatures[UnityEngine.Random.Range(0, eligibleRangedCreatures.Count)];
            pendingDamage.TryGetValue(t.UniqueCreatureID, out int existing);
            int assign = Mathf.Min(dmg, t.Health + t.ShieldValue - existing);
            pendingDamage[t.UniqueCreatureID] = existing + assign;
            if (!attacker.isBuilding)
            {
                pendingDamage.TryGetValue(attacker.id, out int attackerExisting);
                pendingDamage[attacker.id] = attackerExisting + t.Attack;
            }
            else
            {
                pendingBuildingDamage.TryGetValue(attacker.id, out int attackerExisting);
                pendingBuildingDamage[attacker.id] = attackerExisting + t.Attack;
            }
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = t.UniqueCreatureID, targetKind = TargetKind.Creature, damage = assign, targetOwnerPlayerID = defender.PlayerID });
        }

        // Tier 4 : bâtiments ranged
        List<BuildingLogic> eligibleRangedBuildings = new List<BuildingLogic>();
        foreach (BuildingLogic b in buildings)
        {
            if (b.IsMelee || IsEffectivelyDeadBuilding(b)) continue;
            eligibleRangedBuildings.Add(b);
        }
        if (eligibleRangedBuildings.Count > 0)
        {
            BuildingLogic b = eligibleRangedBuildings[UnityEngine.Random.Range(0, eligibleRangedBuildings.Count)];
            pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int existing);
            int assign = Mathf.Min(dmg, b.Health - existing);
            pendingBuildingDamage[b.UniqueBuildingID] = existing + assign;
            if (b.Attack > 0)
            {
                if (!attacker.isBuilding)
                {
                    pendingDamage.TryGetValue(attacker.id, out int attackerExisting);
                    pendingDamage[attacker.id] = attackerExisting + b.Attack;
                }
                else
                {
                    pendingBuildingDamage.TryGetValue(attacker.id, out int attackerExisting);
                    pendingBuildingDamage[attacker.id] = attackerExisting + b.Attack;
                }
            }
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = b.UniqueBuildingID, targetKind = TargetKind.Building, damage = assign, targetOwnerPlayerID = defender.PlayerID });
        }

        BaseLogic defenderBase = FindDefenderBaseInZone(defender);
        if (defenderBase != null)
        {
            pendingBaseDamage.TryGetValue(defenderBase.ID, out int existing);
            pendingBaseDamage[defenderBase.ID] = existing + dmg;
            Debug.Log($"[Battle→Base] attaquant={attacker.id} cible base={defenderBase.ID} ({defenderBase.DisplayName}) dégâts={dmg}");
            return (0, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = defenderBase.ID, targetKind = TargetKind.Base, damage = dmg, targetOwnerPlayerID = defender.PlayerID });
        }
        if (zoneView.subZones.Contains(defender.MainPArea))
        {
            pendingPlayerDamage.TryGetValue(defender.PlayerID, out int existing);
            pendingPlayerDamage[defender.PlayerID] = existing + dmg;
            Debug.Log($"[Battle→Player] attaquant={attacker.id} cible joueur={defender.name} dégâts={dmg}");
            return (0, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = defender.PlayerID, targetKind = TargetKind.Player, damage = dmg, targetOwnerPlayerID = defender.PlayerID });
        }
        return (dmg, null);
    }


    void EnqueueBattleCommands(List<BattleStepRecord> steps)
    {
        foreach (BattleStepRecord step in steps)
        {
            int attackerHP = GetAttackerCurrentHP(step);
            switch (step.targetKind)
            {
                case TargetKind.Creature:
                {
                    if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.targetID, out CreatureLogic target)) continue;
                    CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic attackerCreature);

                    int shieldAbsorbed = Mathf.Min(step.damage, target.ShieldValue);
                    int effectiveDamage = step.damage - shieldAbsorbed;
                    int targetHealthAfter = Mathf.Max(0, target.Health - effectiveDamage);
                    // Debug.Log($"[Shield/Resolver] {target.DisplayName} — Dégâts bruts: {step.damage} | Shield: {target.ShieldValue} | Absorbés: {shieldAbsorbed} | Dégâts effectifs: {effectiveDamage} | PV avant: {target.Health} | PV après: {targetHealthAfter}");

                    int counterDamage = target.Attack;
                    int attackerShieldAbsorbed = (!step.attackerIsBuilding && attackerCreature != null)
                        ? Mathf.Min(counterDamage, attackerCreature.ShieldValue) : 0;
                    int effectiveCounterDamage = counterDamage - attackerShieldAbsorbed;
                    int attackerHealthAfter = Mathf.Max(0, attackerHP - effectiveCounterDamage);
                    // Debug.Log($"[Shield/Resolver] {(attackerCreature != null ? attackerCreature.DisplayName : step.attackerID.ToString())} (attaquant) — Contre-dégâts: {counterDamage} | Shield: {(attackerCreature != null ? attackerCreature.ShieldValue : 0)} | Absorbés: {attackerShieldAbsorbed} | PV avant: {attackerHP} | PV après: {attackerHealthAfter}");

                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter).AddToQueue();
                    else
                        new BuildingAttackCommand(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter).AddToQueue();

                    if (targetHealthAfter <= 0)
                        target.ScheduleBattleDeath();
                    else
                        target.Health -= step.damage;

                    if (!step.attackerIsBuilding && attackerCreature != null)
                    {
                        if (attackerHealthAfter <= 0)
                            attackerCreature.ScheduleBattleDeath();
                        else
                            attackerCreature.Health -= counterDamage;
                    }
                    else if (step.attackerIsBuilding)
                    {
                        BuildingLogic.BuildingsCreatedThisGame.TryGetValue(step.attackerID, out BuildingLogic attackerBuilding);
                        if (attackerBuilding != null)
                        {
                            if (attackerHealthAfter <= 0)
                                attackerBuilding.Die();
                            else
                                attackerBuilding.Health = attackerHealthAfter;
                        }
                    }
                    break;
                }
                case TargetKind.Building:
                {
                    if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(step.targetID, out BuildingLogic target)) continue;
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
                    int counterDamage = target.Attack;
                    int attackerHealthAfter = Mathf.Max(0, attackerHP - counterDamage);
                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter).AddToQueue();
                    else
                        new BuildingAttackCommand(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter).AddToQueue();
                    if (targetHealthAfter > 0)
                        target.Health = targetHealthAfter;
                    else
                        target.Die();
                    if (!step.attackerIsBuilding)
                    {
                        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic attackerCreature))
                        {
                            if (attackerHealthAfter <= 0)
                                attackerCreature.ScheduleBattleDeath();
                            else if (counterDamage > 0)
                                attackerCreature.Health -= counterDamage;
                        }
                    }
                    else
                    {
                        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(step.attackerID, out BuildingLogic attackerBuilding))
                        {
                            if (attackerHealthAfter <= 0)
                                attackerBuilding.Die();
                            else if (counterDamage > 0)
                                attackerBuilding.Health = attackerHealthAfter;
                        }
                    }
                    break;
                }
                case TargetKind.Base:
                {
                    if (!BaseLogic.BasesCreatedThisGame.TryGetValue(step.targetID, out BaseLogic target))
                    {
                        Debug.LogWarning($"[EnqueueBase] Base introuvable id={step.targetID} — step ignoré !");
                        continue;
                    }
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(step.targetID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    else
                        new BuildingAttackCommand(step.targetID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    if (target.IsHomeBase || targetHealthAfter > 0)
                        target.Health = targetHealthAfter;
                    else
                        target.Die();
                    break;
                }
                case TargetKind.Player:
                {
                    Player target = step.targetOwnerPlayerID == GlobalSettings.Instance.LowPlayer.PlayerID
                        ? GlobalSettings.Instance.LowPlayer
                        : GlobalSettings.Instance.TopPlayer;
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
                    Debug.Log($"[EnqueuePlayer] joueur={target.name} HP avant={target.Health} dégâts={step.damage} → HP après={targetHealthAfter}");
                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(target.PlayerID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    else
                        new BuildingAttackCommand(target.PlayerID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    target.Health = targetHealthAfter;
                    break;
                }
            }
        }
    }

    public void EnqueueZoneClashMove()
    {
        bool anyCombat = pendingDamage.Count > 0 || pendingBaseDamage.Count > 0
                      || pendingPlayerDamage.Count > 0 || pendingBuildingDamage.Count > 0;
        List<(int creatureID, Vector3 targetPos)> moves = new();
        List<CreatureLogic> allCreatures = new();
        allCreatures.AddRange(GetCreaturesInMyZone(GlobalSettings.Instance.LowPlayer, zoneView));
        allCreatures.AddRange(GetCreaturesInMyZone(GlobalSettings.Instance.TopPlayer, zoneView));
        foreach (CreatureLogic creature in allCreatures)
        {
            PlayerArea area = FindAreaForCreature(creature);
            if (area?.BattlePos != null)
                moves.Add((creature.UniqueCreatureID, area.BattlePos.position));
        }
        if (moves.Count > 0 && anyCombat)
            new ZoneClashMoveCommand(moves, 0.2f).AddToQueue();
    }

    public static void SerializeAllBattleSteps(
        out int[] resolverIdxs, out int[] attackerIDs, out int[] isBuilding,
        out int[] targetIDs, out int[] targetKinds, out int[] damages, out int[] ownerPlayerIDs)
    {
        List<int> ri = new(); List<int> ai = new();
        List<int> ib = new(); List<int> ti = new();
        List<int> tk = new(); List<int> dg = new();
        List<int> op = new();

        for (int i = 0; i < allResolvers.Count; i++)
        {
            if (allResolvers[i]._lastBattleSteps == null) continue;
            foreach (BattleStepRecord s in allResolvers[i]._lastBattleSteps)
            {
                ri.Add(i);  ai.Add(s.attackerID);
                ib.Add(s.attackerIsBuilding ? 1 : 0);
                ti.Add(s.targetID);
                tk.Add((int)s.targetKind);
                dg.Add(s.damage);
                op.Add(s.targetOwnerPlayerID);
            }
        }
        resolverIdxs   = ri.ToArray(); attackerIDs    = ai.ToArray();
        isBuilding     = ib.ToArray(); targetIDs      = ti.ToArray();
        targetKinds    = tk.ToArray(); damages        = dg.ToArray();
        ownerPlayerIDs = op.ToArray();
    }

    public static void EnqueueAllReconstructedBattleCommands(
        int[] resolverIdxs, int[] attackerIDs, int[] isBuilding,
        int[] targetIDs, int[] targetKinds, int[] damages, int[] ownerPlayerIDs)
    {
        Dictionary<int, List<BattleStepRecord>> stepsByResolver = new();
        for (int i = 0; i < resolverIdxs.Length; i++)
        {
            int rIdx = resolverIdxs[i];
            if (!stepsByResolver.ContainsKey(rIdx))
                stepsByResolver[rIdx] = new List<BattleStepRecord>();
            stepsByResolver[rIdx].Add(new BattleStepRecord
            {
                attackerID          = attackerIDs[i],
                attackerIsBuilding  = isBuilding[i] != 0,
                targetID            = targetIDs[i],
                targetKind          = (TargetKind)targetKinds[i],
                damage              = damages[i],
                targetOwnerPlayerID = ownerPlayerIDs[i]
            });
        }

        foreach (KeyValuePair<int, List<BattleStepRecord>> kvp in stepsByResolver)
        {
            if (kvp.Key < 0 || kvp.Key >= allResolvers.Count) continue;
            allResolvers[kvp.Key].EnqueueBattleCommands(kvp.Value);
        }
    }

    int GetAttackerCurrentHP(BattleStepRecord step)
    {
        if (!step.attackerIsBuilding)
            return CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic c) ? c.Health : 0;
        return BuildingLogic.BuildingsCreatedThisGame.TryGetValue(step.attackerID, out BuildingLogic b) ? b.Health : 0;
    }

    // Une unité qui a reçu des dégâts létaux en séquence ne peut plus attaquer
    bool IsAttackerDead((int attack, bool isBuilding, int id) attacker)
    {
        if (!attacker.isBuilding)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attacker.id, out CreatureLogic c)) return true;
            return IsEffectivelyDead(c);
        }
        if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(attacker.id, out BuildingLogic b)) return true;
        return IsEffectivelyDeadBuilding(b);
    }

    // IsPendingDeath couvre les morts de BattleStart ; pendingDamage >= Health couvre les morts en séquence de Battle
    bool IsEffectivelyDead(CreatureLogic c)
    {
        if (c.IsPendingDeath) return true;
        if (!pendingDamage.TryGetValue(c.UniqueCreatureID, out int d)) return false;
        int effectiveDamage = Mathf.Max(0, d - c.ShieldValue);
        return effectiveDamage >= c.Health;
    }

    bool IsEffectivelyDeadBuilding(BuildingLogic b)
    {
        return pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int d) && d >= b.Health;
    }

    List<CreatureLogic> GetCreaturesInMyZone(Player player, ZoneManager zone)
    {
        List<CreatureLogic> result = new();
        foreach (PlayerArea pa in zone.subZones)
        {
            if (pa.owner == GetAreaPosition(player))
            {
                foreach (CreatureLogic c in player.playedCards.Creatures)
                    if (c.BaseID == pa.baseID)
                        result.Add(c);
            }
        }
        return result;
    }

    List<BuildingLogic> GetAllBuildingsInMyZone(Player player, ZoneManager zone)
    {
        List<BuildingLogic> result = new();
        foreach (BuildingLogic bl in player.playedCards.Buildings)
            if (bl.OriginSpot?.Zone == zone)
                result.Add(bl);
        return result;
    }

    List<BuildingLogic> GetBuildingsInMyZone(Player player, ZoneManager zone)
    {
        List<BuildingLogic> result = new();
        foreach (BuildingLogic bl in player.playedCards.Buildings)
            if (bl.Attack > 0 && bl.OriginSpot != null && bl.OriginSpot.Zone == zone)
                result.Add(bl);
        return result;
    }



    void ClearAllIndicators()
    {
        p1FreePool = 0;
        p2FreePool = 0;
        pendingBuildingDamage.Clear();
    }

    AreaPosition GetAreaPosition(Player player)
    {
        return player == GlobalSettings.Instance.LowPlayer ? AreaPosition.Low : AreaPosition.Top;
    }

    public static bool WouldSurvive(CreatureLogic creature)
    {
        if (creature.IsPendingDeath)
            return false;
        foreach (ZoneCombatResolver r in allResolvers)
            if (r.pendingDamage.TryGetValue(creature.UniqueCreatureID, out int dmg))
            {
                int effectiveDamage = Mathf.Max(0, dmg - creature.ShieldValue);
                return effectiveDamage < creature.Health;
            }
        return true; // no pending damage → survives
    }
    public int GetRemainingPool(AreaPosition attackerSide)
    {
        return attackerSide == AreaPosition.Low ? p1FreePool : p2FreePool;
    }
    BaseLogic FindDefenderBaseInZone(Player defender)
    {
        foreach (BaseLogic _base in BaseLogic.BasesCreatedThisGame.Values)
        {
            if (_base.owner != defender) continue;
            if (_base.neutralBaseController != null && _base.neutralBaseController.zone == zoneView)
                return _base;
        }
        return null;
    }
    void RefreshAllAreaStats()
    {
        foreach (PlayerArea pa in zoneView.subZones)
            pa.RefreshAreaStats();
    }

    // Called from OneCreatureManager click — finds which resolver owns a baseID
    public static ZoneCombatResolver FindForBase(int baseID)
    {
        foreach (ZoneCombatResolver r in allResolvers)
            if (r.OwnsCreature(baseID)) return r;
        return null;
    }

    public bool OwnsCreature(int baseID)
    {
        foreach (PlayerArea pa in zoneView.subZones)
            if (pa.baseID == baseID) return true;
        return false;
    }

    Player GetOwnerPlayer(CreatureLogic creature)
    {
        foreach (PlayerArea pa in zoneView.subZones)
        {
            if (pa.baseID != creature.BaseID) continue;
            return pa.owner == AreaPosition.Low
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
        }
        return null;
    }


    // -------------------------------------------------------------------------
    // SYNCHRONISATION RÉSEAU — ATTRIBUTION DES DÉGÂTS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Regroupe les attributions de dégâts d'un joueur sous forme de tableaux sérialisables,
    /// prêts à être envoyés au serveur via un RPC.
    /// Chaque joueur ne sérialise QUE les dégâts qu'il contrôle (ses propres attaques
    /// ciblant les entités ennemies), car l'autre joueur gère son propre pool d'attaque.
    /// </summary>
    public struct BattleAssignment
    {
        public int[] CreatureIDs;
        public int[] CreatureDamages;
        public int[] BaseIDs;
        public int[] BaseDamages;
        public int[] TargetPlayerIDs;
        public int[] PlayerDamages;
        public int[] BuildingIDs;
        public int[] BuildingDamages;
        public int[] ResolverP1Pools;
        public int[] ResolverP2Pools;
    }

    public static BattleAssignment SerializeMyAttackAssignments(int attackerPlayerIndex)
    {
        Player attacker = Player.Players[attackerPlayerIndex];

        // On cherche l'ennemi : le joueur qui reçoit les attaques de 'attacker'
        Player enemy;
        if (attacker == GlobalSettings.Instance.LowPlayer)
            enemy = GlobalSettings.Instance.TopPlayer;
        else
            enemy = GlobalSettings.Instance.LowPlayer;

        List<int> creatureIDList    = new List<int>();
        List<int> creatureDmgList   = new List<int>();
        List<int> baseIDList        = new List<int>();
        List<int> baseDmgList       = new List<int>();
        List<int> playerIDList      = new List<int>();
        List<int> playerDmgList     = new List<int>();
        List<int> buildingIDList  = new List<int>();
        List<int> buildingDmgList = new List<int>();

        foreach (ZoneCombatResolver resolver in allResolvers)
        {
            foreach (KeyValuePair<int, int> entry in resolver.pendingDamage)
            {
                if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(entry.Key, out CreatureLogic creature)) continue;
                if (resolver.GetOwnerPlayer(creature) != enemy) continue;
                creatureIDList.Add(entry.Key);
                creatureDmgList.Add(entry.Value);
            }

            foreach (KeyValuePair<int, int> entry in resolver.pendingBaseDamage)
            {
                if (!BaseLogic.BasesCreatedThisGame.TryGetValue(entry.Key, out BaseLogic _base)) continue;
                if (_base.owner != enemy) continue;
                baseIDList.Add(entry.Key);
                baseDmgList.Add(entry.Value);
            }

            foreach (KeyValuePair<int, int> entry in resolver.pendingBuildingDamage)
            {
                if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(entry.Key, out BuildingLogic bl)) continue;
                if (bl.owner != enemy) continue;
                buildingIDList.Add(entry.Key);
                buildingDmgList.Add(entry.Value);
            }

            if (resolver.pendingPlayerDamage.TryGetValue(enemy.PlayerID, out int pendingPlayerDmg))
            {
                playerIDList.Add(enemy.PlayerID);
                playerDmgList.Add(pendingPlayerDmg);
            }
        }

        return new BattleAssignment
        {
            CreatureIDs     = creatureIDList.ToArray(),
            CreatureDamages = creatureDmgList.ToArray(),
            BaseIDs     = baseIDList.ToArray(),
            BaseDamages = baseDmgList.ToArray(),
            TargetPlayerIDs = playerIDList.ToArray(),
            PlayerDamages   = playerDmgList.ToArray(),
            BuildingIDs     = buildingIDList.ToArray(),
            BuildingDamages = buildingDmgList.ToArray()
        };
    }

    /// <summary>
    /// Sérialise l'intégralité de l'état de combat calculé par le serveur (tous les resolvers).
    /// Appelé côté serveur après BuildAutoBattleSequence() pour broadcaster l'état canonique.
    /// </summary>
    public static BattleAssignment SerializeAllAssignments()
    {
        List<int> cIDs  = new(); List<int> cDmgs  = new();
        List<int> bIDs  = new(); List<int> bDmgs  = new();
        List<int> pIDs  = new(); List<int> pDmgs  = new();
        List<int> bdIDs = new(); List<int> bdDmgs = new();

        foreach (ZoneCombatResolver r in allResolvers)
        {
            foreach (KeyValuePair<int, int> kvp in r.pendingDamage)        { cIDs.Add(kvp.Key);  cDmgs.Add(kvp.Value);  }
            foreach (KeyValuePair<int, int> kvp in r.pendingBaseDamage)    { bIDs.Add(kvp.Key);  bDmgs.Add(kvp.Value);  }
            foreach (KeyValuePair<int, int> kvp in r.pendingPlayerDamage)  { pIDs.Add(kvp.Key);  pDmgs.Add(kvp.Value);  }
            foreach (KeyValuePair<int, int> kvp in r.pendingBuildingDamage){ bdIDs.Add(kvp.Key); bdDmgs.Add(kvp.Value); }
        }

        int[] p1Pools = new int[allResolvers.Count];
        int[] p2Pools = new int[allResolvers.Count];
        for (int i = 0; i < allResolvers.Count; i++)
        {
            p1Pools[i] = allResolvers[i].p1FreePool;
            p2Pools[i] = allResolvers[i].p2FreePool;
        }

        return new BattleAssignment
        {
            CreatureIDs     = cIDs.ToArray(),  CreatureDamages = cDmgs.ToArray(),
            BaseIDs         = bIDs.ToArray(),  BaseDamages     = bDmgs.ToArray(),
            TargetPlayerIDs = pIDs.ToArray(),  PlayerDamages   = pDmgs.ToArray(),
            BuildingIDs     = bdIDs.ToArray(), BuildingDamages = bdDmgs.ToArray(),
            ResolverP1Pools = p1Pools,
            ResolverP2Pools = p2Pools
        };
    }

    /// <summary>
    /// Applique les valeurs d'overflow (freePool) reçues du serveur sur chaque resolver local.
    /// Appelé après ApplyCanonicalAssignment() pour synchroniser l'affichage UI de l'overflow.
    /// </summary>
    public static void ApplyCanonicalPools(int[] p1Pools, int[] p2Pools)
    {
        for (int i = 0; i < allResolvers.Count && i < p1Pools.Length; i++)
        {
            allResolvers[i].p1FreePool = p1Pools[i];
            allResolvers[i].p2FreePool = p2Pools[i];
        }
        foreach (ZoneCombatResolver r in allResolvers)
            r.RefreshAllAreaStats();
    }

    /// <summary>
    /// Applique l'attribution canonique envoyée par le serveur, en remplacement
    /// complet des dictionnaires locaux de tous les resolvers.
    /// Doit être appelé avant OnBattlePhaseEnd() pour garantir que les dégâts
    /// appliqués sont identiques sur tous les clients.
    /// </summary>
    public static void ApplyCanonicalAssignment(
        int[] creatureIDs,     int[] creatureDamages,
        int[] baseIDs,         int[] baseDamages,
        int[] targetPlayerIDs, int[] playerDamages,
        int[] buildingIDs,     int[] buildingDamages)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
        {
            resolver.pendingDamage.Clear();
            resolver.pendingBaseDamage.Clear();
            resolver.pendingPlayerDamage.Clear();
            resolver.pendingBuildingDamage.Clear();
        }

        for (int i = 0; i < creatureIDs.Length; i++)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureIDs[i], out CreatureLogic creature)) continue;
            ZoneCombatResolver ownerResolver = FindForBase(creature.BaseID);
            if (ownerResolver != null) ownerResolver.pendingDamage[creatureIDs[i]] = creatureDamages[i];
        }

        for (int i = 0; i < baseIDs.Length; i++)
        {
            if (!BaseLogic.BasesCreatedThisGame.TryGetValue(baseIDs[i], out BaseLogic _base)) continue;
            ZoneCombatResolver ownerResolver = FindResolverForBase(_base);
            if (ownerResolver != null) ownerResolver.pendingBaseDamage[baseIDs[i]] = baseDamages[i];
        }

        for (int i = 0; i < targetPlayerIDs.Length; i++)
        {
            Player targetPlayer = targetPlayerIDs[i] == GlobalSettings.Instance.LowPlayer.PlayerID
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
            ZoneCombatResolver ownerResolver = FindResolverForPlayer(targetPlayer);
            if (ownerResolver != null) ownerResolver.pendingPlayerDamage[targetPlayerIDs[i]] = playerDamages[i];
        }

        for (int i = 0; i < buildingIDs.Length; i++)
        {
            if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(buildingIDs[i], out BuildingLogic bl)) continue;
            ZoneCombatResolver ownerResolver = FindResolverForBuilding(bl);
            if (ownerResolver != null) ownerResolver.pendingBuildingDamage[buildingIDs[i]] = buildingDamages[i];
        }
    }

    static ZoneCombatResolver FindResolverForBase(BaseLogic _base)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
            if (_base.neutralBaseController?.zone == resolver.zoneView) return resolver;
        return null;
    }

    static ZoneCombatResolver FindResolverForPlayer(Player player)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
            if (resolver.zoneView.subZones.Contains(player.MainPArea)) return resolver;
        return null;
    }

    public static ZoneCombatResolver FindForBuilding(BuildingLogic bl)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
            if (bl.OriginSpot?.Zone == resolver.zoneView) return resolver;
        return null;
    }

    static ZoneCombatResolver FindResolverForBuilding(BuildingLogic bl) => FindForBuilding(bl);

    void OnDestroy()
    {
        allResolvers.Remove(this);
    }
}
