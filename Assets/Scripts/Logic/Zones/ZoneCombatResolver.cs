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

    public bool HasPossibleCombat()
    {
        Player p1 = GlobalSettings.Instance.LowPlayer;
        Player p2 = GlobalSettings.Instance.TopPlayer;
        bool p1HasBuildingAtk = GetBuildingsInMyZone(p1, zoneLogic).Count > 0;
        bool p2HasBuildingAtk = GetBuildingsInMyZone(p2, zoneLogic).Count > 0;       
        int p1CreatureCount = GetCreaturesInMyZone(p1, zoneLogic).Count;
        int p2CreatureCount = GetCreaturesInMyZone(p2, zoneLogic).Count;

        if (
            (p1CreatureCount > 0 && p2CreatureCount > 0) ||
            (FindDefenderBaseInZone(p1) != null && p2CreatureCount > 0) ||
            (FindDefenderBaseInZone(p2) != null && p1CreatureCount > 0) ||
            (zoneLogic.subZones.Contains(p1.MainPArea) && p2CreatureCount > 0) ||
            (zoneLogic.subZones.Contains(p2.MainPArea) && p1CreatureCount > 0) ||
            (p1HasBuildingAtk && p2CreatureCount > 0) ||
            (p2HasBuildingAtk && p1CreatureCount > 0) ||
            (p1HasBuildingAtk && FindDefenderBaseInZone(p2) != null) ||
            (p2HasBuildingAtk && FindDefenderBaseInZone(p1) != null) ||
            (p1HasBuildingAtk && zoneLogic.subZones.Contains(p2.MainPArea)) ||
            (p2HasBuildingAtk && zoneLogic.subZones.Contains(p1.MainPArea))
        )
            return true;
        return false;
    }

    public void OnBattlePhaseStart()
    {
        pendingDamage.Clear();
        pendingBaseDamage.Clear();
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
        foreach (var b in GetAllBuildingsInMyZone(p2, zoneLogic))
            if (pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int d)) p1Assigned += d;
        p1FreePool = p1TotalATK - p1Assigned;

        int p2Assigned = 0;
        foreach (var c in GetCreaturesInMyZone(p1, zoneLogic))
            if (pendingDamage.TryGetValue(c.UniqueCreatureID, out int d)) p2Assigned += d;
        foreach (var b in GetAllBuildingsInMyZone(p1, zoneLogic))
            if (pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int d)) p2Assigned += d;
        p2FreePool = p2TotalATK - p2Assigned;

        // Subtract overflow already routed to player bases
        foreach (var kvp in pendingBaseDamage)
        {
            if (!BaseLogic.BasesCreatedThisGame.TryGetValue(kvp.Key, out BaseLogic bl)) continue;
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
        bool anyCombat = pendingDamage.Count > 0 || pendingBaseDamage.Count > 0 || pendingPlayerDamage.Count > 0 || pendingBuildingDamage.Count > 0;
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
        {
            foreach (PlayerArea pa in zoneLogic.subZones)
                if (pa.tableVisual != null)
                    new RefreshTableSlotsCommand(pa.tableVisual).AddToQueue();   
        }

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

        bool p1HasAtk = p1Creatures.Count > 0 || GetBuildingsInMyZone(p1, zone).Count > 0;
        bool p2HasAtk = p2Creatures.Count > 0 || GetBuildingsInMyZone(p2, zone).Count > 0;
        if (!p1HasAtk && !p2HasAtk) return;

        p1TotalATK = 0;
        foreach (var c in p1Creatures) p1TotalATK += c.Attack;
        foreach (var b in GetBuildingsInMyZone(p1, zone)) p1TotalATK += b.Attack;

        p2TotalATK = 0;
        foreach (var c in p2Creatures) p2TotalATK += c.Attack;
        foreach (var b in GetBuildingsInMyZone(p2, zone)) p2TotalATK += b.Attack;


        AssignDamage(p1TotalATK, p2Creatures, GetAllBuildingsInMyZone(p2, zone), p2);
        AssignDamage(p2TotalATK, p1Creatures, GetAllBuildingsInMyZone(p1, zone), p1);
    }

    List<CreatureLogic> GetCreaturesInMyZone(Player player, ZoneLogic zone)
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

    List<BuildingLogic> GetAllBuildingsInMyZone(Player player, ZoneLogic zone)
    {
        var result = new List<BuildingLogic>();
        foreach (BuildingLogic bl in player.playedCards.Buildings)
            if (bl.OriginSpot?.Zone == zone)
                result.Add(bl);
        return result;
    }

    List<BuildingLogic> GetBuildingsInMyZone(Player player, ZoneLogic zone)
    {
        var result = new List<BuildingLogic>();
        foreach (BuildingLogic bl in player.playedCards.Buildings)
            if (bl.Attack > 0 && bl.OriginSpot != null && bl.OriginSpot.Zone == zone)
                result.Add(bl);
        return result;
    }

    void AssignDamage(int totalDamage, List<CreatureLogic> creatureTargets, List<BuildingLogic> buildingTargets, Player defender)
    {
        var meleeCreatures = new List<CreatureLogic>();
        var nonMeleeCreatures = new List<CreatureLogic>();
        foreach (var c in creatureTargets) (c.IsMelee ? meleeCreatures : nonMeleeCreatures).Add(c);

        var meleeBuildings = new List<BuildingLogic>();
        var nonMeleeBuildings = new List<BuildingLogic>();
        foreach (var b in buildingTargets) (b.IsMelee ? meleeBuildings : nonMeleeBuildings).Add(b);

        // Phase 1 : front line melee (créatures + bâtiments)
        foreach (var target in meleeCreatures)
        {
            if (totalDamage <= 0) break;
            int dmg = Mathf.Min(totalDamage, target.Health);
            pendingDamage[target.UniqueCreatureID] = dmg;
            totalDamage -= dmg;
            ShowIndicator(target, dmg);
        }
        foreach (var target in meleeBuildings)
        {
            if (totalDamage <= 0) break;
            int dmg = Mathf.Min(totalDamage, target.Health);
            pendingBuildingDamage[target.UniqueBuildingID] = dmg;
            totalDamage -= dmg;
            ShowBuildingIndicator(target, dmg);
        }

        bool allMeleeCreaturesDead = meleeCreatures.TrueForAll(m =>
            pendingDamage.TryGetValue(m.UniqueCreatureID, out int d) && d >= m.Health);
        bool allMeleeBuildingsDead = meleeBuildings.TrueForAll(b =>
            pendingBuildingDamage.TryGetValue(b.UniqueBuildingID, out int d) && d >= b.Health);

        if (!allMeleeCreaturesDead || !allMeleeBuildingsDead) return;

        // Phase 2 : non-melee
        foreach (var target in nonMeleeCreatures)
        {
            if (totalDamage <= 0) break;
            int dmg = Mathf.Min(totalDamage, target.Health);
            pendingDamage[target.UniqueCreatureID] = dmg;
            totalDamage -= dmg;
            ShowIndicator(target, dmg);
        }
        foreach (var target in nonMeleeBuildings)
        {
            if (totalDamage <= 0) break;
            int dmg = Mathf.Min(totalDamage, target.Health);
            pendingBuildingDamage[target.UniqueBuildingID] = dmg;
            totalDamage -= dmg;
            ShowBuildingIndicator(target, dmg);
        }

        if (totalDamage <= 0) return;

        // Overflow vers base/joueur
        BaseLogic _base = FindDefenderBaseInZone(defender);
        if (_base != null)
        {
            int existing = pendingBaseDamage.TryGetValue(_base.ID, out int b2) ? b2 : 0;
            pendingBaseDamage[_base.ID] = existing + totalDamage;
            ShowBaseIndicator(_base.ID, existing + totalDamage, _base.Health);
        }
        else if (zoneLogic.subZones.Contains(defender.MainPArea))
        {
            int existing = pendingPlayerDamage.TryGetValue(defender.PlayerID, out int p) ? p : 0;
            pendingPlayerDamage[defender.PlayerID] = existing + totalDamage;
            ShowBaseIndicator(defender.PlayerID, existing + totalDamage, defender.Health);
        }
    }


    void ShowIndicator(CreatureLogic creature, int damage)
    {
        if (GlobalSettings.Instance.localPlayer.playedCards.Creatures.Contains(creature)) return;
        GameObject go = IDHolder.GetGameObjectWithID(creature.UniqueCreatureID);
        go?.GetComponent<OneCreatureManager>()?.ShowPendingDamage(damage, creature.Health);
    }

    void ShowBaseIndicator(int id, int damage, int health)
    {
        Player local = GlobalSettings.Instance.localPlayer;
        if (id == local.PlayerID) return;
        if (BaseLogic.BasesCreatedThisGame.TryGetValue(id, out BaseLogic bl) && bl.owner == local) return;
        IDHolder.GetGameObjectWithID(id)?.GetComponent<OneBaseManager>()?.ShowPendingDamage(damage, health);
    }

    void ShowBuildingIndicator(BuildingLogic building, int damage)
    {
        GameObject go = IDHolder.GetGameObjectWithID(building.UniqueBuildingID);
        go?.GetComponent<OneBuildingManager>()?.ShowPendingDamage(damage, building.Health);
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
        foreach (var bl in BaseLogic.BasesCreatedThisGame.Values)
        {
            GameObject go = IDHolder.GetGameObjectWithID(bl.ID);
            go?.GetComponent<OneBaseManager>()?.ClearPendingDamageIndicator();
        }
        foreach (var bl in BuildingLogic.BuildingsCreatedThisGame.Values)
        {
            GameObject go = IDHolder.GetGameObjectWithID(bl.UniqueBuildingID);
            go?.GetComponent<OneBuildingManager>()?.ClearPendingDamageIndicator();
        }
        pendingBuildingDamage.Clear();
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
    BaseLogic FindDefenderBaseInZone(Player defender)
    {
        foreach (var _base in BaseLogic.BasesCreatedThisGame.Values)
        {
            if (_base.owner != defender) continue;
            if (_base.neutralBaseController != null && _base.neutralBaseController.zone == zoneLogic)
                return _base;
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
        if (!aliveNonFatalMeleeExists)
        {
            foreach (var b in GetAllBuildingsInMyZone(owner, zoneLogic))
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
            if (bl2.neutralBaseController?.zone != zoneLogic) return;
        }
        else if (!zoneLogic.subZones.Contains(defender.MainPArea)) return;

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
            if (_base.neutralBaseController?.zone == resolver.zoneLogic) return resolver;
        return null;
    }

    static ZoneCombatResolver FindResolverForPlayer(Player player)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
            if (resolver.zoneLogic.subZones.Contains(player.MainPArea)) return resolver;
        return null;
    }

    public static ZoneCombatResolver FindForBuilding(BuildingLogic bl)
    {
        foreach (ZoneCombatResolver resolver in allResolvers)
            if (bl.OriginSpot?.Zone == resolver.zoneLogic) return resolver;
        return null;
    }

    static ZoneCombatResolver FindResolverForBuilding(BuildingLogic bl) => FindForBuilding(bl);

    public void TryRedirectDamageFromBuilding(BuildingLogic building)
    {
        int id = building.UniqueBuildingID;
        Player defender = building.owner;
        if (defender == null) return;
        if (GlobalSettings.Instance.localPlayer == defender) return;
        if (building.OriginSpot == null || building.OriginSpot.Zone != zoneLogic) return;

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
            foreach (var c in GetCreaturesInMyZone(defender, zoneLogic))
            {
                if (!c.IsMelee) continue;
                bool fatal = pendingDamage.TryGetValue(c.UniqueCreatureID, out int d) && d >= c.Health;
                if (!fatal) return;
            }
            foreach (var b in GetAllBuildingsInMyZone(defender, zoneLogic))
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

    void OnDestroy()
    {
        allResolvers.Remove(this);
    }
}
