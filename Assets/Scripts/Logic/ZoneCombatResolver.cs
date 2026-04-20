using System.Collections.Generic;
using UnityEngine;

public class ZoneCombatResolver : MonoBehaviour
{
    private Dictionary<int, int> pendingDamage = new Dictionary<int, int>();
    private static List<ZoneCombatResolver> allResolvers = new List<ZoneCombatResolver>();
    private Dictionary<int, int> pendingBuildingDamage = new Dictionary<int, int>();
    private Dictionary<int, int> pendingPlayerDamage   = new Dictionary<int, int>();

    private ZoneLogic zoneLogic;
    private int p1TotalATK;
    private int p2TotalATK;
    private int p1FreePool;
    private int p2FreePool;

    void Awake()
    {
        zoneLogic = GetComponent<ZoneLogic>();
        allResolvers.Add(this);
    }

    public void OnBattlePhaseStart()
    {
        pendingDamage.Clear();
        pendingBuildingDamage.Clear();
        pendingPlayerDamage.Clear();
        p1TotalATK = 0;
        p2TotalATK = 0;
        p1FreePool = 0;
        p2FreePool = 0;
        ResolveZone(zoneLogic);

        // Any ATK that couldn't be placed (exceeds total enemy health) starts in the free pool
        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;
        int p1Assigned = 0;
        foreach (var c in GetCreaturesInMyZone(p2, zoneLogic))
            if (pendingDamage.TryGetValue(c.UniqueCreatureID, out int d)) p1Assigned += d;
        p1FreePool = p1TotalATK - p1Assigned;

        int p2Assigned = 0;
        foreach (var c in GetCreaturesInMyZone(p1, zoneLogic))
            if (pendingDamage.TryGetValue(c.UniqueCreatureID, out int d)) p2Assigned += d;
        p2FreePool = p2TotalATK - p2Assigned;
        // Subtract overflow already routed to buildings or player bases
        foreach (var kvp in pendingBuildingDamage)
        {
            if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(kvp.Key, out BuildingLogic bl)) continue;
            if (bl.owner == p2) p1FreePool -= kvp.Value;
            else                p2FreePool -= kvp.Value;
        }
        foreach (var kvp in pendingPlayerDamage)
        {
            if (kvp.Key == p2.PlayerID) p1FreePool -= kvp.Value;
            else                        p2FreePool -= kvp.Value;
        }
        p1FreePool = Mathf.Max(0, p1FreePool);
        p2FreePool = Mathf.Max(0, p2FreePool);

        RefreshAllAreaStats();
    }

    public void OnBattlePhaseEnd()
    {
        // Collect all creatures in the zone and their battle positions
        var moves = new List<(int creatureID, Vector3 targetPos)>();
        var allCreatures = new List<CreatureLogic>();
        allCreatures.AddRange(GetCreaturesInMyZone(GlobalSettings.Instance.LowPlayer, zoneLogic));
        allCreatures.AddRange(GetCreaturesInMyZone(GlobalSettings.Instance.TopPlayer, zoneLogic));

        foreach (var creature in allCreatures)
        {
            PlayerArea area = FindAreaForCreature(creature);
            if (area?.BattlePos != null)
                moves.Add((creature.UniqueCreatureID, area.BattlePos.position));
        }

        bool anyCombat = pendingDamage.Count > 0 || pendingBuildingDamage.Count > 0 || pendingPlayerDamage.Count > 0;
        if (moves.Count > 0 && anyCombat)
            new ZoneClashMoveCommand(moves, 0.2f).AddToQueue();


        // Deal damage
        foreach (var kvp in pendingDamage)
        {
            CreatureLogic creature = CreatureLogic.CreaturesCreatedThisGame[kvp.Key];
            int damage = kvp.Value;
            int healthAfter = creature.Health - damage;
            new DealDamageCommand(kvp.Key, damage, healthAfter).AddToQueue();
            creature.Health -= damage;
        }

        foreach (var kvp in pendingBuildingDamage)
        {
            if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(kvp.Key, out BuildingLogic bl)) continue;
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

        // Return survivors to their slots
        foreach (PlayerArea pa in zoneLogic.subZones)
            if (pa.tableVisual != null)
                new RefreshTableSlotsCommand(pa.tableVisual).AddToQueue();

        pendingDamage.Clear();
        ClearAllIndicators();
    }

    PlayerArea FindAreaForCreature(CreatureLogic creature)
    {
        foreach (PlayerArea pa in zoneLogic.subZones)
            if (pa.baseID == creature.BaseID)
                return pa;
        return null;
    }

    void ResolveZone(ZoneLogic zone)
    {
        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;

        List<CreatureLogic> p1Creatures = GetCreaturesInMyZone(p1, zone);
        List<CreatureLogic> p2Creatures = GetCreaturesInMyZone(p2, zone);

        if (p1Creatures.Count == 0 && p2Creatures.Count == 0) return;

        p1TotalATK = 0;
        foreach (var c in p1Creatures) p1TotalATK += c.Attack;

        p2TotalATK = 0;
        foreach (var c in p2Creatures) p2TotalATK += c.Attack;

        AssignDamage(p1TotalATK, p2Creatures, p2);
        AssignDamage(p2TotalATK, p1Creatures, p1);
    }

    List<CreatureLogic> GetCreaturesInMyZone(Player player, ZoneLogic zone)
    {
        var result = new List<CreatureLogic>();
        foreach (PlayerArea pa in zone.subZones)
        {
            if (pa.owner == GetAreaPosition(player))
            {
                foreach (CreatureLogic c in player.table.CreaturesInPlay)
                    if (c.BaseID == pa.baseID)
                        result.Add(c);
            }
        }
        return result;
    }

    void AssignDamage(int totalDamage, List<CreatureLogic> targets, Player defender)
    {
        var melee = new List<CreatureLogic>();
        var nonMelee = new List<CreatureLogic>();
        foreach (var t in targets)
            (t.IsMelee ? melee : nonMelee).Add(t);

        foreach (var target in melee)
        {
            if (totalDamage <= 0) break;
            int dmg = Mathf.Min(totalDamage, target.Health);
            pendingDamage[target.UniqueCreatureID] = dmg;
            totalDamage -= dmg;
            ShowIndicator(target, dmg);
        }

        bool allMeleeDead = melee.TrueForAll(m =>
            pendingDamage.TryGetValue(m.UniqueCreatureID, out int d) && d >= m.Health);

        if (!allMeleeDead) return;

        foreach (var target in nonMelee)
        {
            if (totalDamage <= 0) break;
            int dmg = Mathf.Min(totalDamage, target.Health);
            pendingDamage[target.UniqueCreatureID] = dmg;
            totalDamage -= dmg;
            ShowIndicator(target, dmg);
        }

        if (totalDamage <= 0) return;

        // Every creature is lethally hit (or there were none) — overflow to base
        BuildingLogic building = FindDefenderBuildingInZone(defender);
        if (building != null)
        {
            int existing = pendingBuildingDamage.TryGetValue(building.UniqueBuildingID, out int b) ? b : 0;
            pendingBuildingDamage[building.UniqueBuildingID] = existing + totalDamage;
            ShowBaseIndicator(building.UniqueBuildingID, existing + totalDamage, building.Health);

        }
        else
        {
            // Check this zone actually contains the defender's main base before going face
            AreaPosition defenderPos = GetAreaPosition(defender);
            bool hasAreaHere = false;
            foreach (PlayerArea pa in zoneLogic.subZones)
                if (pa.owner == defenderPos) { hasAreaHere = true; break; }

            if (hasAreaHere)
            {
                int existing = pendingPlayerDamage.TryGetValue(defender.PlayerID, out int p) ? p : 0;
                pendingPlayerDamage[defender.PlayerID] = existing + totalDamage;
                ShowBaseIndicator(defender.PlayerID, existing + totalDamage, defender.Health);

            }
        }

    }

    void ShowIndicator(CreatureLogic creature, int damage)
    {
        if (GlobalSettings.Instance.localPlayer.table.CreaturesInPlay.Contains(creature)) return;
        GameObject go = IDHolder.GetGameObjectWithID(creature.UniqueCreatureID);
        go?.GetComponent<OneCreatureManager>()?.ShowPendingDamage(damage, creature.Health);
    }

    void ShowBaseIndicator(int id, int damage, int health)
    {
        Player local = GlobalSettings.Instance.localPlayer;
        if (id == local.PlayerID) return;
        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(id, out BuildingLogic bl) && bl.owner == local) return;
        IDHolder.GetGameObjectWithID(id)?.GetComponent<OneBuildingManager>()?.ShowPendingDamage(damage, health);
    }


    void ClearAllIndicators()
    {
        p1FreePool = 0;
        p2FreePool = 0;
        foreach (CreatureLogic c in CreatureLogic.CreaturesCreatedThisGame.Values)
        {
            GameObject go = IDHolder.GetGameObjectWithID(c.UniqueCreatureID);
            go?.GetComponent<OneCreatureManager>()?.ClearPendingDamageIndicator();
        }
        foreach (var bl in BuildingLogic.BuildingsCreatedThisGame.Values)
        {
            GameObject go = IDHolder.GetGameObjectWithID(bl.UniqueBuildingID);
            go?.GetComponent<OneBuildingManager>()?.ClearPendingDamageIndicator();
        }
    }

    AreaPosition GetAreaPosition(Player player)
    {
        return player == GlobalSettings.Instance.LowPlayer ? AreaPosition.Low : AreaPosition.Top;
    }

    public static bool WouldSurvive(CreatureLogic creature)
    {
        foreach (var r in allResolvers)
            if (r.pendingDamage.TryGetValue(creature.UniqueCreatureID, out int dmg))
                return dmg < creature.Health;
        return true; // no pending damage → survives
    }
    public int GetRemainingPool(AreaPosition attackerSide)
    {
        return attackerSide == AreaPosition.Low ? p1FreePool : p2FreePool;
    }
    BuildingLogic FindDefenderBuildingInZone(Player defender)
    {
        foreach (var building in BuildingLogic.BuildingsCreatedThisGame.Values)
        {
            if (building.owner != defender) continue;
            if (building.neutralBaseController != null && building.neutralBaseController.zone == zoneLogic)
                return building;
        }
        return null;
    }
    void RefreshAllAreaStats()
    {
        foreach (PlayerArea pa in zoneLogic.subZones)
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
        foreach (PlayerArea pa in zoneLogic.subZones)
            if (pa.baseID == baseID) return true;
        return false;
    }

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
        List<CreatureLogic> allTargets = GetCreaturesInMyZone(owner, zoneLogic);

        bool aliveNonFatalMeleeExists = false;
        foreach (var t in allTargets)
        {
            if (!t.IsMelee) continue;
            bool fatal = pendingDamage.TryGetValue(t.UniqueCreatureID, out int d) && d >= t.Health;
            if (!fatal) { aliveNonFatalMeleeExists = true; break; }
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

    AreaPosition GetCreatureSide(CreatureLogic creature)
    {
        foreach (PlayerArea pa in zoneLogic.subZones)
            if (pa.baseID == creature.BaseID) return pa.owner;
        return AreaPosition.Neutral;
    }

    Player GetOwnerPlayer(CreatureLogic creature)
    {
        foreach (PlayerArea pa in zoneLogic.subZones)
        {
            if (pa.baseID != creature.BaseID) continue;
            return pa.owner == AreaPosition.Low
                ? GlobalSettings.Instance.LowPlayer
                : GlobalSettings.Instance.TopPlayer;
        }
        return null;
    }

    public void TryRedirectDamageFromBase(int targetID)
    {
        bool isBuilding = BuildingLogic.BuildingsCreatedThisGame.ContainsKey(targetID);

        Player defender;
        int currentHealth;
        if (isBuilding)
        {
            BuildingLogic bl = BuildingLogic.BuildingsCreatedThisGame[targetID];
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
        var dict = isBuilding ? pendingBuildingDamage : pendingPlayerDamage;

        // Phase 1: base has pending damage → free it back to pool
        if (dict.TryGetValue(targetID, out int freed))
        {
            dict.Remove(targetID);
            IDHolder.GetGameObjectWithID(targetID)?.GetComponent<OneBuildingManager>()?.ClearPendingDamageIndicator();
            if (defenderIsTop) p1FreePool += freed;
            else               p2FreePool += freed;
            RefreshAllAreaStats();
            return;
        }

        // Phase 2: assign free pool to this base
        int freePool = defenderIsTop ? p1FreePool : p2FreePool;
        if (freePool <= 0) return;

        // Only allowed if all MELEE creatures of the defender are already lethally hit
        foreach (var c in GetCreaturesInMyZone(defender, zoneLogic))
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
    void OnDestroy()
    {
        allResolvers.Remove(this);
    }
}
