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

    private enum TargetKind { Creature, Building, Base, Player }
    private struct BattleStepRecord
    {
        public int attackerID;
        public bool attackerIsBuilding;
        public int targetID;
        public TargetKind targetKind;
        public int damage;
        public Player targetOwner;
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
                BuildAutoBattleSequence(zoneView);
        }
        else
        {
            var steps = BuildAutoBattleSequence(zoneView);
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
        if (NetworkSessionData.IsNetworkSession)
        {
            var moves = new List<(int creatureID, Vector3 targetPos)>();
            var allCreatures = new List<CreatureLogic>();
            allCreatures.AddRange(GetCreaturesInMyZone(GlobalSettings.Instance.LowPlayer, zoneView));
            allCreatures.AddRange(GetCreaturesInMyZone(GlobalSettings.Instance.TopPlayer, zoneView));
            foreach (var creature in allCreatures)
            {
                PlayerArea area = FindAreaForCreature(creature);
                if (area?.BattlePos != null)
                    moves.Add((creature.UniqueCreatureID, area.BattlePos.position));
            }
            bool anyCombat = pendingDamage.Count > 0 || pendingBaseDamage.Count > 0 || pendingPlayerDamage.Count > 0 || pendingBuildingDamage.Count > 0;
            if (moves.Count > 0 && anyCombat)
                new ZoneClashMoveCommand(moves, 0.2f).AddToQueue();

            foreach (var kvp in pendingDamage)
            {
                CreatureLogic creature = CreatureLogic.CreaturesCreatedThisGame[kvp.Key];
                int damage = kvp.Value;
                int healthAfter = creature.Health - damage;
                new DealDamageCommand(kvp.Key, damage, healthAfter).AddToQueue();
                creature.Health -= damage;
            }
            foreach (var kvp in pendingBaseDamage)
            {
                if (!BaseLogic.BasesCreatedThisGame.TryGetValue(kvp.Key, out BaseLogic bl)) continue;
                int healthAfter = bl.Health - kvp.Value;
                new DealDamageCommand(kvp.Key, kvp.Value, healthAfter).AddToQueue();
                bl.Health -= kvp.Value;
            }
            foreach (var kvp in pendingPlayerDamage)
            {
                Player target = kvp.Key == GlobalSettings.Instance.LowPlayer.PlayerID
                    ? GlobalSettings.Instance.LowPlayer
                    : GlobalSettings.Instance.TopPlayer;
                int healthAfter = target.Health - kvp.Value;
                new DealDamageCommand(target.PlayerID, kvp.Value, healthAfter).AddToQueue();
                target.Health -= kvp.Value;
            }
            foreach (var kvp in pendingBuildingDamage)
            {
                if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(kvp.Key, out BuildingLogic bl)) continue;
                int healthAfter = bl.Health - kvp.Value;
                new DealDamageCommand(kvp.Key, kvp.Value, healthAfter).AddToQueue();
                bl.Health -= kvp.Value;
            }
            if (anyCombat)
                foreach (PlayerArea pa in zoneView.subZones)
                    if (pa.tableVisual != null)
                        new RefreshTableSlotsCommand(pa.tableVisual).AddToQueue();
        }
        else
        {
            // Solo: damage was applied during Battle via commands; just clean up positions
            foreach (PlayerArea pa in zoneView.subZones)
                if (pa.tableVisual != null)
                    new RefreshTableSlotsCommand(pa.tableVisual).AddToQueue();
        }

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
        var steps = new List<BattleStepRecord>();
        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;

        var queue1 = BuildAttackQueue(p1, zone);
        var queue2 = BuildAttackQueue(p2, zone);

        bool p1Turn = UnityEngine.Random.value < 0.5f;
        int i1 = 0, i2 = 0;

        while (i1 < queue1.Count || i2 < queue2.Count)
        {
            bool p1CanAct = i1 < queue1.Count;
            bool p2CanAct = i2 < queue2.Count;

            if (p1CanAct && (!p2CanAct || p1Turn))
            {
                var attacker = queue1[i1++];
                if (!IsAttackerDead(attacker))
                {
                    var (overflow, step) = AssignSingleAttack(attacker, p2, zone);
                    p1FreePool += overflow;
                    if (step.HasValue) steps.Add(step.Value);
                }
            }
            else if (p2CanAct)
            {
                var attacker = queue2[i2++];
                if (!IsAttackerDead(attacker))
                {
                    var (overflow, step) = AssignSingleAttack(attacker, p1, zone);
                    p2FreePool += overflow;
                    if (step.HasValue) steps.Add(step.Value);
                }
            }

            p1CanAct = i1 < queue1.Count;
            p2CanAct = i2 < queue2.Count;
            if (p1CanAct && p2CanAct) p1Turn = !p1Turn;
            else p1Turn = p1CanAct;
        }
        return steps;
    }

    // Ordre : mêlée créatures → non-mêlée créatures → mêlée bâtiments → non-mêlée bâtiments
    List<(int attack, bool isBuilding, int id)> BuildAttackQueue(Player player, ZoneManager zone)
    {
        var result = new List<(int, bool, int)>();
        var creatures = GetCreaturesInMyZone(player, zone);

        foreach (var c in creatures)
            if (c.IsMelee && c.Attack > 0) result.Add((c.Attack, false, c.UniqueCreatureID));
        foreach (var c in creatures)
            if (!c.IsMelee && c.Attack > 0) result.Add((c.Attack, false, c.UniqueCreatureID));

        var buildings = GetBuildingsInMyZone(player, zone);
        foreach (var b in buildings)
            if (b.IsMelee) result.Add((b.Attack, true, b.UniqueBuildingID));
        foreach (var b in buildings)
            if (!b.IsMelee) result.Add((b.Attack, true, b.UniqueBuildingID));

        return result;
    }

    // Retourne le surplus de dégâts non placés (overflow) et la description de l'attaque pour animation
    (int, BattleStepRecord?) AssignSingleAttack((int attack, bool isBuilding, int id) attacker, Player defender, ZoneManager zone)
    {
        int dmg = attacker.attack;
        var creatures = GetCreaturesInMyZone(defender, zone);
        var buildings = GetAllBuildingsInMyZone(defender, zone);

        foreach (var t in creatures)
        {
            if (!t.IsMelee || IsEffectivelyDead(t)) continue;
            pendingDamage.TryGetValue(t.UniqueCreatureID, out int existing);
            int assign = Mathf.Min(dmg, t.Health + t.ShieldValue - existing);
            pendingDamage[t.UniqueCreatureID] = existing + assign;
            if (!attacker.isBuilding)
            {
                pendingDamage.TryGetValue(attacker.id, out int attackerExisting);
                pendingDamage[attacker.id] = attackerExisting + t.Attack;
            }
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = t.UniqueCreatureID, targetKind = TargetKind.Creature, damage = assign, targetOwner = defender });
        }
        foreach (var b in buildings)
        {
            if (!b.IsMelee || IsEffectivelyDeadBuilding(b)) continue;
            pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int existing);
            int assign = Mathf.Min(dmg, b.Health - existing);
            pendingBuildingDamage[b.UniqueBuildingID] = existing + assign;
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = b.UniqueBuildingID, targetKind = TargetKind.Building, damage = assign, targetOwner = defender });
        }
        foreach (var t in creatures)
        {
            if (t.IsMelee || IsEffectivelyDead(t)) continue;
            pendingDamage.TryGetValue(t.UniqueCreatureID, out int existing);
            int assign = Mathf.Min(dmg, t.Health + t.ShieldValue - existing);
            pendingDamage[t.UniqueCreatureID] = existing + assign;
            if (!attacker.isBuilding)
            {
                pendingDamage.TryGetValue(attacker.id, out int attackerExisting);
                pendingDamage[attacker.id] = attackerExisting + t.Attack;
            }
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = t.UniqueCreatureID, targetKind = TargetKind.Creature, damage = assign, targetOwner = defender });
        }
        foreach (var b in buildings)
        {
            if (b.IsMelee || IsEffectivelyDeadBuilding(b)) continue;
            pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int existing);
            int assign = Mathf.Min(dmg, b.Health - existing);
            pendingBuildingDamage[b.UniqueBuildingID] = existing + assign;
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = b.UniqueBuildingID, targetKind = TargetKind.Building, damage = assign, targetOwner = defender });
        }

        BaseLogic defenderBase = FindDefenderBaseInZone(defender);
        if (defenderBase != null)
        {
            pendingBaseDamage.TryGetValue(defenderBase.ID, out int existing);
            pendingBaseDamage[defenderBase.ID] = existing + dmg;
            return (0, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = defenderBase.ID, targetKind = TargetKind.Base, damage = dmg, targetOwner = defender });
        }
        if (zoneView.subZones.Contains(defender.MainPArea))
        {
            pendingPlayerDamage.TryGetValue(defender.PlayerID, out int existing);
            pendingPlayerDamage[defender.PlayerID] = existing + dmg;
            return (0, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = defender.PlayerID, targetKind = TargetKind.Player, damage = dmg, targetOwner = defender });
        }
        return (dmg, null);
    }

    void EnqueueBattleCommands(List<BattleStepRecord> steps)
    {
        foreach (var step in steps)
        {
            int attackerHP = GetAttackerCurrentHP(step);
            switch (step.targetKind)
            {
                case TargetKind.Creature:
                {
                    if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.targetID, out var target)) continue;
                    CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out var attackerCreature);

                    int shieldAbsorbed = Mathf.Min(step.damage, target.ShieldValue);
                    int effectiveDamage = step.damage - shieldAbsorbed;
                    int targetHealthAfter = Mathf.Max(0, target.Health - effectiveDamage);
                    Debug.Log($"[Shield/Resolver] {target.DisplayName} — Dégâts bruts: {step.damage} | Shield: {target.ShieldValue} | Absorbés: {shieldAbsorbed} | Dégâts effectifs: {effectiveDamage} | PV avant: {target.Health} | PV après: {targetHealthAfter}");

                    int counterDamage = step.attackerIsBuilding ? 0 : target.Attack;
                    int attackerShieldAbsorbed = (!step.attackerIsBuilding && attackerCreature != null)
                        ? Mathf.Min(counterDamage, attackerCreature.ShieldValue) : 0;
                    int effectiveCounterDamage = counterDamage - attackerShieldAbsorbed;
                    int attackerHealthAfter = Mathf.Max(0, attackerHP - effectiveCounterDamage);
                    Debug.Log($"[Shield/Resolver] {(attackerCreature != null ? attackerCreature.DisplayName : step.attackerID.ToString())} (attaquant) — Contre-dégâts: {counterDamage} | Shield: {(attackerCreature != null ? attackerCreature.ShieldValue : 0)} | Absorbés: {attackerShieldAbsorbed} | PV avant: {attackerHP} | PV après: {attackerHealthAfter}");

                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter).AddToQueue();
                    else
                        new BuildingAttackCommand(step.targetID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();

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
                    break;
                }
                case TargetKind.Building:
                {
                    if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(step.targetID, out var target)) continue;
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(step.targetID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    else
                        new BuildingAttackCommand(step.targetID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    if (targetHealthAfter > 0)
                        target.Health = targetHealthAfter;
                    else
                        new BuildingDieCommand(step.targetID).AddToQueue();
                    break;
                }
                case TargetKind.Base:
                {
                    if (!BaseLogic.BasesCreatedThisGame.TryGetValue(step.targetID, out var target)) continue;
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(step.targetID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    else
                        new BuildingAttackCommand(step.targetID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    target.Health = targetHealthAfter;
                    if (targetHealthAfter <= 0)
                        new BaseDieCommand(step.targetID, target.neutralBaseController).AddToQueue();
                    break;
                }
                case TargetKind.Player:
                {
                    var target = step.targetOwner;
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
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

    int GetAttackerCurrentHP(BattleStepRecord step)
    {
        if (!step.attackerIsBuilding)
            return CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out var c) ? c.Health : 0;
        return BuildingLogic.BuildingsCreatedThisGame.TryGetValue(step.attackerID, out var b) ? b.Health : 0;
    }

    // Une unité qui a reçu des dégâts létaux en séquence ne peut plus attaquer
    bool IsAttackerDead((int attack, bool isBuilding, int id) attacker)
    {
        if (!attacker.isBuilding)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attacker.id, out var c)) return true;
            return IsEffectivelyDead(c);
        }
        if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(attacker.id, out var b)) return true;
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
        var result = new List<CreatureLogic>();
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
        var result = new List<BuildingLogic>();
        foreach (BuildingLogic bl in player.playedCards.Buildings)
            if (bl.OriginSpot?.Zone == zone)
                result.Add(bl);
        return result;
    }

    List<BuildingLogic> GetBuildingsInMyZone(Player player, ZoneManager zone)
    {
        var result = new List<BuildingLogic>();
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
        foreach (var _base in BaseLogic.BasesCreatedThisGame.Values)
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
        foreach (var r in allResolvers)
            if (r.OwnsCreature(baseID)) return r;
        return null;
    }

    public bool OwnsCreature(int baseID)
    {
        foreach (PlayerArea pa in zoneView.subZones)
            if (pa.baseID == baseID) return true;
        return false;
    }

    /*
    public void TryRedirectDamageFrom(CreatureLogic clicked)
    {
        int id = clicked.UniqueCreatureID;
        AreaPosition clickedSide = GetCreatureSide(clicked);
        if (clickedSide == AreaPosition.Neutral) return;

        // TOP creatures are attacked by LOW (p1) → p1FreePool
        // LOW creatures are attacked by TOP (p2) → p2FreePool
        bool isTopCreature = clickedSide == AreaPosition.Top;

        // Phase 1: creature has pending damage → free it into the pool
        if (pendingDamage.TryGetValue(id, out int freedDamage))
        {
            pendingDamage.Remove(id);
            IDHolder.GetGameObjectWithID(id)?.GetComponent<OneCreatureManager>()?.ClearPendingDamageIndicator();
            if (isTopCreature) p1FreePool += freedDamage;
            else               p2FreePool += freedDamage;
            RefreshAllAreaStats();
            return;
        }

        // Phase 2: no pending damage → try to assign free pool to this creature
        int freePool = isTopCreature ? p1FreePool : p2FreePool;
        if (freePool <= 0) return;

        Player owner = GetOwnerPlayer(clicked);
        if (owner == null) return;
        List<CreatureLogic> allTargets = GetCreaturesInMyZone(owner, zoneView);

        bool aliveNonFatalMeleeExists = false;
        foreach (var t in allTargets)
        {
            if (!t.IsMelee) continue;
            bool fatal = pendingDamage.TryGetValue(t.UniqueCreatureID, out int d) && d >= t.Health;
            if (!fatal) { aliveNonFatalMeleeExists = true; break; }
        }
        if (!aliveNonFatalMeleeExists)
        {
            foreach (var b in GetAllBuildingsInMyZone(owner, zoneView))
            {
                if (!b.IsMelee) continue;
                bool fatal = pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int d) && d >= b.Health;
                if (!fatal) { aliveNonFatalMeleeExists = true; break; }
            }
        }
        if (aliveNonFatalMeleeExists && !clicked.IsMelee) return;

        int existing = pendingDamage.TryGetValue(id, out int existingDmg) ? existingDmg : 0;
        if (existing >= clicked.Health) return;

        int assign = Mathf.Min(freePool, clicked.Health - existing);
        pendingDamage[id] = existing + assign;
        if (isTopCreature) p1FreePool -= assign;
        else               p2FreePool -= assign;
        ShowIndicator(clicked, pendingDamage[id]);
        RefreshAllAreaStats();
    }
    */

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

    /*
    public void TryRedirectDamageFromBase(int targetID)
    {
        bool isBase = BaseLogic.BasesCreatedThisGame.ContainsKey(targetID);

        Player defender;
        int currentHealth;
        if (isBase)
        {
            BaseLogic bl = BaseLogic.BasesCreatedThisGame[targetID];
            defender = bl.owner;
            currentHealth = bl.Health;
        }
        else
        {
            defender = targetID == GlobalSettings.Instance.LowPlayer.PlayerID
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
            currentHealth = defender.Health;
        }

        if (GlobalSettings.Instance.localPlayer == defender) return;

        bool defenderIsTop = defender == GlobalSettings.Instance.TopPlayer;
        var dict = isBase ? pendingBaseDamage : pendingPlayerDamage;

        // Phase 1: base has pending damage → free it back to pool
        if (dict.TryGetValue(targetID, out int freed))
        {
            dict.Remove(targetID);
            IDHolder.GetGameObjectWithID(targetID)?.GetComponent<OneBaseManager>()?.ClearPendingDamageIndicator();
            if (defenderIsTop) p1FreePool += freed;
            else               p2FreePool += freed;
            RefreshAllAreaStats();
            return;
        }

        // Phase 2: assign free pool to this base
        int freePool = defenderIsTop ? p1FreePool : p2FreePool;
        if (freePool <= 0) return;

        // Target must be in this zone
        if (isBase)
        {
            BaseLogic bl2 = BaseLogic.BasesCreatedThisGame[targetID];
            if (bl2.neutralBaseController?.zone != zoneView) return;
        }
        else if (!zoneView.subZones.Contains(defender.MainPArea)) return;

        // Only allowed if all MELEE creatures of the defender are already lethally hit
        foreach (var c in GetCreaturesInMyZone(defender, zoneView))
        {
            if (!c.IsMelee) continue;
            bool fatal = pendingDamage.TryGetValue(c.UniqueCreatureID, out int d) && d >= c.Health;
            if (!fatal) return;
        }

        int existing = dict.TryGetValue(targetID, out int ex) ? ex : 0;
        int assign = Mathf.Min(freePool, currentHealth - existing);
        if (assign <= 0) return;

        dict[targetID] = existing + assign;
        ShowBaseIndicator(targetID, existing + assign, currentHealth);
        if (defenderIsTop) p1FreePool -= assign;
        else               p2FreePool -= assign;
        RefreshAllAreaStats();
    }

    void AssignRemainingPool(int pool, List<CreatureLogic> allTargets)
    {
        var melee = new List<CreatureLogic>();
        var nonMelee = new List<CreatureLogic>();

        foreach (var t in allTargets)
        {
            bool fatallyHit = pendingDamage.TryGetValue(t.UniqueCreatureID, out int d) && d >= t.Health;
            if (fatallyHit) continue;
            (t.IsMelee ? melee : nonMelee).Add(t);
        }

        bool meleeAlive = melee.Count > 0;

        foreach (var t in melee)
        {
            if (pool <= 0) break;
            int existing = pendingDamage.TryGetValue(t.UniqueCreatureID, out int d) ? d : 0;
            int dmg = Mathf.Min(pool, t.Health - existing);
            pendingDamage[t.UniqueCreatureID] = existing + dmg;
            pool -= dmg;
            ShowIndicator(t, pendingDamage[t.UniqueCreatureID]);
        }

        if (meleeAlive) return;

        foreach (var t in nonMelee)
        {
            if (pool <= 0) break;
            int existing = pendingDamage.TryGetValue(t.UniqueCreatureID, out int d) ? d : 0;
            int dmg = Mathf.Min(pool, t.Health - existing);
            pendingDamage[t.UniqueCreatureID] = existing + dmg;
            pool -= dmg;
            ShowIndicator(t, pendingDamage[t.UniqueCreatureID]);
        }

        RefreshAllAreaStats();
    }
    */

    /*private void ColorizeUnits()
    {
        TurnManager turnmanager = TurnManager.Instance;
        if (turnmanager.CurrentPhase != TurnManager.TurnPhases.Battle) {
            return;
        }
        foreach (CreatureLogic cl in playerOwner.otherPlayer.table.CreaturesInPlay)
        {
            GameObject g = IDHolder.GetGameObjectWithID(cl.UniqueCreatureID);
            g.GetComponent<OneCreatureManager>().UpdateTargetableVisual(cl.Targetable);
        }
    }*/

    /*private void ResetColorizeUnits()
    {
        foreach (CreatureLogic cl in playerOwner.otherPlayer.table.CreaturesInPlay)
        {
            GameObject g = IDHolder.GetGameObjectWithID(cl.UniqueCreatureID);
            g.GetComponent<OneCreatureManager>().UpdateTargetableVisual(true);
        }
    }*/
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
        var cIDs  = new List<int>(); var cDmgs  = new List<int>();
        var bIDs  = new List<int>(); var bDmgs  = new List<int>();
        var pIDs  = new List<int>(); var pDmgs  = new List<int>();
        var bdIDs = new List<int>(); var bdDmgs = new List<int>();

        foreach (var r in allResolvers)
        {
            foreach (var kvp in r.pendingDamage)        { cIDs.Add(kvp.Key);  cDmgs.Add(kvp.Value);  }
            foreach (var kvp in r.pendingBaseDamage)    { bIDs.Add(kvp.Key);  bDmgs.Add(kvp.Value);  }
            foreach (var kvp in r.pendingPlayerDamage)  { pIDs.Add(kvp.Key);  pDmgs.Add(kvp.Value);  }
            foreach (var kvp in r.pendingBuildingDamage){ bdIDs.Add(kvp.Key); bdDmgs.Add(kvp.Value); }
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
        foreach (var r in allResolvers)
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

    /*
    public void TryRedirectDamageFromBuilding(BuildingLogic building)
    {
        int id = building.UniqueBuildingID;
        Player defender = building.owner;
        if (defender == null) return;
        if (GlobalSettings.Instance.localPlayer == defender) return;
        if (building.OriginSpot == null || building.OriginSpot.Zone != zoneView) return;

        bool defenderIsTop = defender == GlobalSettings.Instance.TopPlayer;

        // Phase 1 : le bâtiment a des dégâts pending → les libérer dans le pool
        if (pendingBuildingDamage.TryGetValue(id, out int freed))
        {
            pendingBuildingDamage.Remove(id);
            GameObject go = IDHolder.GetGameObjectWithID(id);
            if (go != null && go.TryGetComponent(out OneBuildingManager obm))
                obm.ClearPendingDamageIndicator();
            if (defenderIsTop) p1FreePool += freed;
            else               p2FreePool += freed;
            RefreshAllAreaStats();
            return;
        }

        // Phase 2 : assigner du pool libre vers ce bâtiment
        int freePool = defenderIsTop ? p1FreePool : p2FreePool;
        if (freePool <= 0) return;

        // Contrainte melee : cible non-melee seulement si tous les corps-à-corps ennemis sont déjà lethalement touchés
        if (!building.IsMelee)
        {
            foreach (var c in GetCreaturesInMyZone(defender, zoneView))
            {
                if (!c.IsMelee) continue;
                bool fatal = pendingDamage.TryGetValue(c.UniqueCreatureID, out int d) && d >= c.Health;
                if (!fatal) return;
            }
            foreach (var b in GetAllBuildingsInMyZone(defender, zoneView))
            {
                if (!b.IsMelee) continue;
                bool fatal = pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int d) && d >= b.Health;
                if (!fatal) return;
            }
        }

        int existing = pendingBuildingDamage.TryGetValue(id, out int ex) ? ex : 0;
        int assign = Mathf.Min(freePool, building.Health - existing);
        if (assign <= 0) return;

        pendingBuildingDamage[id] = existing + assign;
        ShowBuildingIndicator(building, existing + assign);
        if (defenderIsTop) p1FreePool -= assign;
        else               p2FreePool -= assign;
        RefreshAllAreaStats();
    }
    */

    void OnDestroy()
    {
        allResolvers.Remove(this);
    }
}
