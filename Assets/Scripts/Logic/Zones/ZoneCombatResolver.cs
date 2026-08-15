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

    // Files d'attaque en cours de planification (non nulles seulement pendant l'exécution de
    // BuildAutoBattleSequence) — permet à un token créé à la volée (TokenGenerationSO, via un
    // OnDeath résolu par anticipation, voir CreatureLogic.ResolvePredictedBattleDeath) de
    // rejoindre CETTE bataille au lieu d'attendre le prochain combat.
    private List<(int attack, bool isBuilding, int id)> _planningQueueP1;
    private List<(int attack, bool isBuilding, int id)> _planningQueueP2;

    // Triplets (source, index d'effet, seed) accumulés côté serveur pendant
    // BuildAutoBattleSequence, pour chaque OnDeath résolu par anticipation en planification
    // (CreatureLogic.ResolvePredictedBattleDeath) — diffusés aux clients pour rejeu déterministe
    // via EffectRegistry.Execute (même mécanisme que PhaseEffectPipeline.ApplyCanonicalResolution).
    public struct OnDeathBattleReplay
    {
        public int SourceCreatureID;
        public int EffectIndex;
        public int Seed;
    }
    private static readonly List<OnDeathBattleReplay> _pendingOnDeathReplays = new();

    public static void RecordOnDeathBattleReplay(int sourceCreatureID, int effectIndex, int seed)
    {
        _pendingOnDeathReplays.Add(new OnDeathBattleReplay
            { SourceCreatureID = sourceCreatureID, EffectIndex = effectIndex, Seed = seed });
    }

    // Consommé une seule fois par SubmitBattleAssignmentServerRpc, juste après que toute la
    // planification (tous resolvers) soit terminée, pour sérialiser vers les clients.
    public static List<OnDeathBattleReplay> DrainOnDeathBattleReplays()
    {
        List<OnDeathBattleReplay> snapshot = new List<OnDeathBattleReplay>(_pendingOnDeathReplays);
        _pendingOnDeathReplays.Clear();
        return snapshot;
    }

    private enum TargetKind { Creature, Building, Base, Player }
    private struct BattleStepRecord
    {
        public int attackerID;
        public bool attackerIsBuilding;
        public int targetID;
        public TargetKind targetKind;
        public int damage;
        public int targetOwnerPlayerID;
        // Cibles secondaires (AttackModifierSO) déjà résolues pendant la planification — ID + dégât brut.
        // Diffusées telles quelles à tous les clients ; EnqueueBattleCommands ne recalcule plus aucun ciblage.
        public List<AttackHitResult> secondaryHits;
        // Contre-dégâts subis par l'attaquant, figés au moment de la planification (même valeur que
        // celle passée à AddPendingCreatureDamage pour prédire une éventuelle mort de l'attaquant).
        // EnqueueBattleCommands ne relit plus target.Attack en direct : ça appliquerait à une contre-
        // attaque déjà causalement figée un buff/debuff survenu plus tard dans la séquence planifiée.
        public int counterDamage;
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
        _planningQueueP1 = queue1;
        _planningQueueP2 = queue2;

        int p1ZoneUnitCount = GetCreaturesInMyZone(p1, zone).Count;
        int p2ZoneUnitCount = GetCreaturesInMyZone(p2, zone).Count;
        bool p1Turn = p1ZoneUnitCount != p2ZoneUnitCount
            ? p1ZoneUnitCount > p2ZoneUnitCount
            : UnityEngine.Random.value < 0.5f;
        Debug.Log($"[Sequence:{zoneView.name}] Commence : {(p1Turn ? p1.name : p2.name)} | queue1={queue1.Count} queue2={queue2.Count}");
        int i1 = 0, i2 = 0, stepNum = 0;

        while (true)
        {
            // pendingDamage inclut désormais les dégâts de zone prédits (ReserveModifierSplashDamage),
            // donc ce skip peut être causé par une mort prédite via un modificateur, pas seulement par
            // une attaque directe classique.
            while (i1 < queue1.Count && IsAttackerDead(queue1[i1]))
            {
                pendingDamage.TryGetValue(queue1[i1].id, out int reserved);
                Debug.Log($"  [Skip mort:{zoneView.name}] {p1.name} — ID:{queue1[i1].id} (pendingDamage:{reserved}) — tour annulé");
                i1++;
            }
            while (i2 < queue2.Count && IsAttackerDead(queue2[i2]))
            {
                pendingDamage.TryGetValue(queue2[i2].id, out int reserved);
                Debug.Log($"  [Skip mort:{zoneView.name}] {p2.name} — ID:{queue2[i2].id} (pendingDamage:{reserved}) — tour annulé");
                i2++;
            }

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
        Debug.Log($"[Sequence:{zoneView.name}] {steps.Count} step(s) générés au total");
        _planningQueueP1 = null;
        _planningQueueP2 = null;
        return steps;
    }

    // Appelé par TokenGenerationSO quand un token est créé à la volée. Si ce baseID est
    // couvert par un resolver actuellement en train de planifier sa bataille (voir
    // BuildAutoBattleSequence / _planningQueueP1-P2), le token rejoint la file d'attaque de son
    // propriétaire pour pouvoir encore attaquer dans cette même bataille.
    public static void NotifyCreatureAddedDuringPlanning(CreatureLogic creature)
    {
        if (creature.Attack <= 0) return;
        ZoneCombatResolver resolver = FindForBase(creature.BaseID);
        if (resolver == null) return;

        List<(int attack, bool isBuilding, int id)> queue =
            creature.owner == GlobalSettings.Instance.LowPlayer ? resolver._planningQueueP1
            : creature.owner == GlobalSettings.Instance.TopPlayer ? resolver._planningQueueP2
            : null;
        queue?.Add((creature.Attack, false, creature.UniqueCreatureID));
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
        // Lecture live (pas la valeur figée dans le tuple attacker au tout début de la zone) :
        // si cet attaquant a été buffé par un OnDeath résolu plus tôt dans cette même
        // planification (voir AddPendingCreatureDamage), les dégâts qu'il inflige doivent déjà
        // le refléter tant qu'il n'a pas encore été traité par cette méthode.
        int dmg = GetLiveAttack(attacker.id, attacker.isBuilding, attacker.attack);
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
            Debug.Log($"[Assign:{zoneView.name}][Tier1:BâtimentMêlée] attaquant={attacker.id}({(attacker.isBuilding ? "bât" : "créat")}) cible bât={b.UniqueBuildingID}({b.DisplayName}) dégâts={assign} overflow={dmg - assign}");
            int counter1 = 0;
            if (b.Attack > 0 && IsMeleeAttacker(attacker.id, attacker.isBuilding))
            {
                counter1 = b.Attack;
                if (!attacker.isBuilding)
                    AddPendingCreatureDamage(attacker.id, b.Attack);
                else
                {
                    pendingBuildingDamage.TryGetValue(attacker.id, out int attackerExisting);
                    pendingBuildingDamage[attacker.id] = attackerExisting + b.Attack;
                }
            }
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = b.UniqueBuildingID, targetKind = TargetKind.Building, damage = assign, targetOwnerPlayerID = defender.PlayerID, counterDamage = counter1 });
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
            AddPendingCreatureDamage(t.UniqueCreatureID, assign);
            Debug.Log($"[Assign:{zoneView.name}][Tier2:CréatureMêlée] attaquant={attacker.id}({(attacker.isBuilding ? "bât" : "créat")}) cible créat={t.UniqueCreatureID}({t.DisplayName}) dégâts={assign} overflow={dmg - assign}");
            int counter2 = 0;
            if (IsMeleeAttacker(attacker.id, attacker.isBuilding))
            {
                counter2 = t.Attack;
                if (!attacker.isBuilding)
                    AddPendingCreatureDamage(attacker.id, t.Attack);
                else
                {
                    pendingBuildingDamage.TryGetValue(attacker.id, out int attackerExisting);
                    pendingBuildingDamage[attacker.id] = attackerExisting + t.Attack;
                }
            }
            List<AttackHitResult> tier2SecondaryHits = ResolveAndReserveModifierHits(attacker.id, attacker.isBuilding, t);
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = t.UniqueCreatureID, targetKind = TargetKind.Creature, damage = assign, targetOwnerPlayerID = defender.PlayerID, secondaryHits = tier2SecondaryHits, counterDamage = counter2 });
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
            AddPendingCreatureDamage(t.UniqueCreatureID, assign);
            Debug.Log($"[Assign:{zoneView.name}][Tier3:CréatureRanged] attaquant={attacker.id}({(attacker.isBuilding ? "bât" : "créat")}) cible créat={t.UniqueCreatureID}({t.DisplayName}) dégâts={assign} overflow={dmg - assign}");
            int counter3 = 0;
            if (IsMeleeAttacker(attacker.id, attacker.isBuilding))
            {
                counter3 = t.Attack;
                if (!attacker.isBuilding)
                    AddPendingCreatureDamage(attacker.id, t.Attack);
                else
                {
                    pendingBuildingDamage.TryGetValue(attacker.id, out int attackerExisting);
                    pendingBuildingDamage[attacker.id] = attackerExisting + t.Attack;
                }
            }
            List<AttackHitResult> tier3SecondaryHits = ResolveAndReserveModifierHits(attacker.id, attacker.isBuilding, t);
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = t.UniqueCreatureID, targetKind = TargetKind.Creature, damage = assign, targetOwnerPlayerID = defender.PlayerID, secondaryHits = tier3SecondaryHits, counterDamage = counter3 });
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
            Debug.Log($"[Assign:{zoneView.name}][Tier4:BâtimentRanged] attaquant={attacker.id}({(attacker.isBuilding ? "bât" : "créat")}) cible bât={b.UniqueBuildingID}({b.DisplayName}) dégâts={assign} overflow={dmg - assign}");
            int counter4 = 0;
            if (b.Attack > 0 && IsMeleeAttacker(attacker.id, attacker.isBuilding))
            {
                counter4 = b.Attack;
                if (!attacker.isBuilding)
                    AddPendingCreatureDamage(attacker.id, b.Attack);
                else
                {
                    pendingBuildingDamage.TryGetValue(attacker.id, out int attackerExisting);
                    pendingBuildingDamage[attacker.id] = attackerExisting + b.Attack;
                }
            }
            return (dmg - assign, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = b.UniqueBuildingID, targetKind = TargetKind.Building, damage = assign, targetOwnerPlayerID = defender.PlayerID, counterDamage = counter4 });
        }

        BaseLogic defenderBase = FindDefenderBaseInZone(defender);
        if (defenderBase != null)
        {
            pendingBaseDamage.TryGetValue(defenderBase.ID, out int existing);
            pendingBaseDamage[defenderBase.ID] = existing + dmg;
            Debug.Log($"[Battle→Base:{zoneView.name}] attaquant={attacker.id} cible base={defenderBase.ID} ({defenderBase.DisplayName}) dégâts={dmg}");
            return (0, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = defenderBase.ID, targetKind = TargetKind.Base, damage = dmg, targetOwnerPlayerID = defender.PlayerID });
        }
        if (zoneView.subZones.Contains(defender.MainPArea))
        {
            pendingPlayerDamage.TryGetValue(defender.PlayerID, out int existing);
            pendingPlayerDamage[defender.PlayerID] = existing + dmg;
            Debug.Log($"[Battle→Player:{zoneView.name}] attaquant={attacker.id} cible joueur={defender.name} dégâts={dmg}");
            return (0, new BattleStepRecord { attackerID = attacker.id, attackerIsBuilding = attacker.isBuilding, targetID = defender.PlayerID, targetKind = TargetKind.Player, damage = dmg, targetOwnerPlayerID = defender.PlayerID });
        }
        Debug.Log($"[Assign:{zoneView.name}][AucuneCible] attaquant={attacker.id}({(attacker.isBuilding ? "bât" : "créat")}) dégâts={dmg} perdus — aucune cible éligible (créatures/bâtiments/base/joueur)");
        return (dmg, null);
    }

    // Résout, UNE SEULE FOIS pendant la planification (côté serveur en réseau), les cibles secondaires
    // de tous les AttackModifierSO (Lateral/Row/Cone/Piercing/Circular/Ricochet) de cet attaquant, et
    // les injecte dans pendingDamage — le même dictionnaire déjà utilisé pour l'anti-surkill
    // (AssignSingleAttack) et pour prédire les morts en séquence (IsEffectivelyDead / IsAttackerDead),
    // qui sert ici aussi de "isDead" pour la résolution des modificateurs eux-mêmes (une cible tuée par
    // un attaquant précédent dans cette même passe ne peut plus être retouchée/reciblée).
    // Le résultat figé (ID + dégât brut) est stocké dans le BattleStepRecord et diffusé à tous les
    // clients : EnqueueBattleCommands ne fait plus AUCUNE décision de ciblage, il ne fait qu'appliquer
    // les dégâts aux IDs déjà donnés — élimine tout risque de désync sur "qui se fait toucher".
    List<AttackHitResult> ResolveAndReserveModifierHits(int attackerID, bool attackerIsBuilding, CreatureLogic mainTarget)
    {
        List<AttackHitResult> allHits = new List<AttackHitResult>();
        if (attackerIsBuilding) return allHits; // seules les créatures ont des AttackModifiers
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic attackerCreature)) return allHits;
        if (attackerCreature.AttackModifiers == null) return allHits;

        foreach (AttackModifierSO mod in attackerCreature.AttackModifiers)
        {
            if (mod == null) continue; // slot vide/cassé — voir le même garde-fou dans EnqueueBattleCommands
            List<AttackHitResult> resolved = mod.ResolveTargets(attackerCreature, mainTarget, IsEffectivelyDead);
            if (resolved == null) continue;

            foreach (AttackHitResult hit in resolved)
            {
                pendingDamage.TryGetValue(hit.TargetUniqueID, out int existing);
                AddPendingCreatureDamage(hit.TargetUniqueID, hit.Damage);
                allHits.Add(hit);
                Debug.Log($"[Resolve:{zoneView.name}] {mod.GetType().Name} — attaquant={attackerID}({attackerCreature.DisplayName}) → cible={hit.TargetUniqueID} dégâts={hit.Damage} (pendingDamage: {existing} → {existing + hit.Damage})");
            }
        }
        return allHits;
    }

    void EnqueueBattleCommands(List<BattleStepRecord> steps)
    {
        Debug.Log($"[Enqueue:{zoneView.name}] Traitement de {steps.Count} step(s)");
        int stepIdx = 0;
        foreach (BattleStepRecord step in steps)
        {
            stepIdx++;
            int attackerHP = GetAttackerCurrentHP(step);
            switch (step.targetKind)
            {
                case TargetKind.Creature:
                {
                    if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.targetID, out CreatureLogic target)) continue;
                    CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic attackerCreature);

                    // Un attaquant déjà IsPendingDeath ici peut avoir deux causes : (1) il est mort lors
                    // d'un step ANTÉRIEUR de cette même liste — son tour ne doit alors plus s'exécuter
                    // (comportement historique) — ou (2) il vient de mourir EN RÉTORSION, pendant la
                    // construction même de CE step : l'OnDeath de sa propre cible, résolu par
                    // anticipation dans AssignSingleAttack/AddPendingCreatureDamage (voir
                    // CreatureLogic.ResolvePredictedBattleDeath), l'a tué avant même que ce
                    // BattleStepRecord ne soit construit. Dans ce second cas, l'attaque a bel et bien
                    // eu lieu — c'est elle qui a déclenché la mort de la cible et donc son OnDeath — et
                    // sauter tout le step empêcherait target.ScheduleBattleDeath() de jamais s'exécuter,
                    // laissant la cible "vivante" pour de bon et son report différé (Command.DeferForBattleReplay)
                    // bloqué indéfiniment. On ne peut pas distinguer les deux cas ici, mais laisser le
                    // step s'exécuter normalement est sans risque dans les deux cas : les mutations de
                    // l'attaquant plus bas (ScheduleBattleDeath/Health) sont déjà protégées par son
                    // propre IsPendingDeath et no-opent proprement s'il est déjà mort.
                    if (target.IsPendingDeath)
                        Debug.LogWarning($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} — cible {target.UniqueCreatureID}({target.DisplayName}) DÉJÀ IsPendingDeath au moment du traitement (tuée par un effet secondaire ?) — contre-attaque fantôme possible");

                    int shieldAbsorbed = Mathf.Min(step.damage, target.ShieldValue);
                    int effectiveDamage = step.damage - shieldAbsorbed;
                    int targetHealthAfter = Mathf.Max(0, target.Health - effectiveDamage);
                    // Debug.Log($"[Shield/Resolver] {target.DisplayName} — Dégâts bruts: {step.damage} | Shield: {target.ShieldValue} | Absorbés: {shieldAbsorbed} | Dégâts effectifs: {effectiveDamage} | PV avant: {target.Health} | PV après: {targetHealthAfter}");

                    int counterDamage = step.counterDamage;
                    int attackerShieldAbsorbed = (!step.attackerIsBuilding && attackerCreature != null)
                        ? Mathf.Min(counterDamage, attackerCreature.ShieldValue) : 0;
                    int effectiveCounterDamage = counterDamage - attackerShieldAbsorbed;
                    int attackerHealthAfter = Mathf.Max(0, attackerHP - effectiveCounterDamage);
                    // Debug.Log($"[Shield/Resolver] {(attackerCreature != null ? attackerCreature.DisplayName : step.attackerID.ToString())} (attaquant) — Contre-dégâts: {counterDamage} | Shield: {(attackerCreature != null ? attackerCreature.ShieldValue : 0)} | Absorbés: {attackerShieldAbsorbed} | PV avant: {attackerHP} | PV après: {attackerHealthAfter}");

                    // Cibles secondaires (Cone/Piercing/Circular/...) déjà résolues UNE FOIS pendant la
                    // planification (ResolveAndReserveModifierHits) et transportées telles quelles dans le
                    // step — aucun ciblage n'est recalculé ici, seulement l'application des dégâts (bouclier/
                    // PV après calculés en live, comme pour la cible principale, pour rester exact même si
                    // l'état a bougé depuis la planification). Calculé avant d'empiler la commande principale,
                    // pour que la liste transportée avec elle soit déjà complète — la commande peut s'exécuter
                    // de façon synchrone dès AddToQueue() (file vide + caméra déjà en position), donc la
                    // remplir après coup risquerait qu'elle soit lue vide par la séquence visuelle.
                    // Important : la mutation ci-dessous NE programme PAS la mort des cibles secondaires —
                    // ScheduleBattleDeath() est appelé plus bas, après avoir mis en file la commande d'attaque
                    // principale, pour que la CreatureDieCommand d'une cible secondaire ne s'exécute jamais
                    // avant l'animation qui est censée la tuer.
                    List<AttackHitResult> secondaryHits = new List<AttackHitResult>();
                    if (step.secondaryHits != null)
                        foreach (AttackHitResult reserved in step.secondaryHits)
                        {
                            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(reserved.TargetUniqueID, out CreatureLogic secTarget)) continue;
                            if (secTarget.IsPendingDeath) continue; // défensif — ne devrait pas arriver si la résolution planifiée est cohérente
                            int secShieldAbs = Mathf.Min(reserved.Damage, secTarget.ShieldValue);
                            int secEffective = reserved.Damage - secShieldAbs;
                            int secHealthAfter = Mathf.Max(0, secTarget.Health - secEffective);
                            secondaryHits.Add(new AttackHitResult(secTarget.UniqueCreatureID, reserved.Damage, secHealthAfter));
                            if (secHealthAfter > 0)
                                secTarget.Health -= secEffective;
                        }

                    Debug.Log($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} Creature — attaquant={step.attackerID} cible={target.UniqueCreatureID}({target.DisplayName}) dégâts={step.damage} PVcibleAprès={targetHealthAfter} contreDégâts={counterDamage} PVattaquantAprès={attackerHealthAfter}");
                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter, attackerCreature?.AttackSpeedMultiplier ?? 1f, secondaryHits).AddToQueue();
                    else
                        new BuildingAttackCommand(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter).AddToQueue();

                    if (targetHealthAfter <= 0)
                        target.ScheduleBattleDeath();
                    else
                        target.Health -= effectiveDamage;

                    if (!step.attackerIsBuilding && attackerCreature != null)
                    {
                        if (attackerHealthAfter <= 0)
                            attackerCreature.ScheduleBattleDeath();
                        else
                            attackerCreature.Health -= effectiveCounterDamage;
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

                    // Mort des cibles secondaires tuées par un modificateur — mise en file seulement maintenant,
                    // après la CreatureAttackCommand principale (voir commentaire plus haut).
                    foreach (AttackHitResult hit in secondaryHits)
                    {
                        if (hit.HealthAfter <= 0 && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(hit.TargetUniqueID, out CreatureLogic secondaryCreature))
                            secondaryCreature.ScheduleBattleDeath();
                    }

                    break;
                }
                case TargetKind.Building:
                {
                    if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(step.targetID, out BuildingLogic target)) continue;
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
                    int counterDamage = step.counterDamage;
                    int attackerHealthAfter = Mathf.Max(0, attackerHP - counterDamage);
                    CreatureLogic.CreaturesCreatedThisGame.TryGetValue(step.attackerID, out CreatureLogic atkCr);
                    Debug.Log($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} Building — attaquant={step.attackerID} cible bât={target.UniqueBuildingID}({target.DisplayName}) dégâts={step.damage} PVcibleAprès={targetHealthAfter} contreDégâts={counterDamage} PVattaquantAprès={attackerHealthAfter}");
                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(step.targetID, step.attackerID, counterDamage, step.damage, attackerHealthAfter, targetHealthAfter, atkCr?.AttackSpeedMultiplier ?? 1f).AddToQueue();
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
                        Debug.LogWarning($"[Enqueue:{zoneView.name}][EnqueueBase] step {stepIdx}/{steps.Count} — Base introuvable id={step.targetID} — step ignoré !");
                        continue;
                    }
                    int targetHealthAfter = Mathf.Max(0, target.Health - step.damage);
                    Debug.Log($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} Base — attaquant={step.attackerID} cible base={target.ID}({target.DisplayName}) dégâts={step.damage} PVaprès={targetHealthAfter}");
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
                    Debug.Log($"[Enqueue:{zoneView.name}] step {stepIdx}/{steps.Count} Player — attaquant={step.attackerID} cible joueur={target.name} HP avant={target.Health} dégâts={step.damage} → HP après={targetHealthAfter}");
                    if (!step.attackerIsBuilding)
                        new CreatureAttackCommand(target.PlayerID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    else
                        new BuildingAttackCommand(target.PlayerID, step.attackerID, 0, step.damage, attackerHP, targetHealthAfter).AddToQueue();
                    target.Health = targetHealthAfter;
                    break;
                }
            }
        }
        Debug.Log($"[Enqueue:{zoneView.name}] Terminé — {steps.Count} step(s) traité(s), file de commandes: {Command.CommandQueue.Count} en attente, playingQueue={Command.playingQueue}");
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
        out int[] targetIDs, out int[] targetKinds, out int[] damages, out int[] ownerPlayerIDs,
        out int[] secondaryCounts, out int[] secondaryTargetIDs, out int[] secondaryDamages,
        out int[] counterDamages)
    {
        List<int> ri = new(); List<int> ai = new();
        List<int> ib = new(); List<int> ti = new();
        List<int> tk = new(); List<int> dg = new();
        List<int> op = new();
        List<int> scnt = new(); List<int> stid = new(); List<int> sdmg = new();
        List<int> cd = new();

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
                cd.Add(s.counterDamage);

                int secCount = s.secondaryHits?.Count ?? 0;
                scnt.Add(secCount);
                if (s.secondaryHits != null)
                    foreach (AttackHitResult hit in s.secondaryHits)
                    {
                        stid.Add(hit.TargetUniqueID);
                        sdmg.Add(hit.Damage);
                    }
            }
        }
        resolverIdxs   = ri.ToArray(); attackerIDs    = ai.ToArray();
        isBuilding     = ib.ToArray(); targetIDs      = ti.ToArray();
        targetKinds    = tk.ToArray(); damages        = dg.ToArray();
        ownerPlayerIDs = op.ToArray();
        secondaryCounts    = scnt.ToArray();
        secondaryTargetIDs = stid.ToArray();
        secondaryDamages   = sdmg.ToArray();
        counterDamages     = cd.ToArray();
    }

    public static void EnqueueAllReconstructedBattleCommands(
        int[] resolverIdxs, int[] attackerIDs, int[] isBuilding,
        int[] targetIDs, int[] targetKinds, int[] damages, int[] ownerPlayerIDs,
        int[] secondaryCounts, int[] secondaryTargetIDs, int[] secondaryDamages,
        int[] counterDamages)
    {
        Dictionary<int, List<BattleStepRecord>> stepsByResolver = new();
        int secCursor = 0;
        for (int i = 0; i < resolverIdxs.Length; i++)
        {
            int rIdx = resolverIdxs[i];
            if (!stepsByResolver.ContainsKey(rIdx))
                stepsByResolver[rIdx] = new List<BattleStepRecord>();

            List<AttackHitResult> secondaryHits = new List<AttackHitResult>();
            int secCount = (secondaryCounts != null && i < secondaryCounts.Length) ? secondaryCounts[i] : 0;
            for (int k = 0; k < secCount; k++)
            {
                secondaryHits.Add(new AttackHitResult(secondaryTargetIDs[secCursor], secondaryDamages[secCursor], 0));
                secCursor++;
            }

            stepsByResolver[rIdx].Add(new BattleStepRecord
            {
                attackerID          = attackerIDs[i],
                attackerIsBuilding  = isBuilding[i] != 0,
                targetID            = targetIDs[i],
                targetKind          = (TargetKind)targetKinds[i],
                damage              = damages[i],
                targetOwnerPlayerID = ownerPlayerIDs[i],
                secondaryHits       = secondaryHits,
                counterDamage       = (counterDamages != null && i < counterDamages.Length) ? counterDamages[i] : 0
            });
        }

        foreach (KeyValuePair<int, List<BattleStepRecord>> kvp in stepsByResolver)
        {
            if (kvp.Key < 0 || kvp.Key >= allResolvers.Count) continue;
            try
            {
                allResolvers[kvp.Key].EnqueueBattleCommands(kvp.Value);
            }
            catch (System.Exception e)
            {
                // Sans ce filet, une exception ici saute tous les resolvers suivants dans ce dictionnaire
                // (donc leurs combats entiers) côté client, pour la même raison que dans TurnManager.DelayedBattleStart.
                Debug.LogError($"[EnqueueAllReconstructed] EXCEPTION pour resolver #{kvp.Key} — les resolvers suivants auraient été SAUTÉS sans ce filet: {e}");
            }
        }
    }

    // Les attaquants non-mêlée (ranged) ne subissent pas les dégâts de contre-attaque de leur cible
    bool IsMeleeAttacker(int attackerID, bool attackerIsBuilding)
    {
        if (!attackerIsBuilding)
            return CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic c) && c.IsMelee;
        return BuildingLogic.BuildingsCreatedThisGame.TryGetValue(attackerID, out BuildingLogic b) && b.IsMelee;
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

    // Valeur d'attaque actuelle (live) d'un attaquant identifié par ID — reflète un éventuel
    // buff OnDeath déjà résolu plus tôt dans cette même planification. fallbackValue est utilisé
    // si l'entité est introuvable (ne devrait pas arriver, garde défensive).
    int GetLiveAttack(int id, bool isBuilding, int fallbackValue)
    {
        if (!isBuilding)
            return CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic c) ? c.Attack : fallbackValue;
        return BuildingLogic.BuildingsCreatedThisGame.TryGetValue(id, out BuildingLogic b) ? b.Attack : fallbackValue;
    }

    // Ajoute `amount` aux dégâts prédits (pendingDamage) de la créature `creatureID`. Si cette
    // mise à jour la fait franchir le seuil de mort prédite pour la première fois, résout son
    // OnDeath immédiatement (voir CreatureLogic.ResolvePredictedBattleDeath) — avant qu'aucune
    // attaque suivante de la séquence ne soit assignée, pour que le buff éventuel s'applique
    // aux dégâts et à la survie du reste du combat.
    void AddPendingCreatureDamage(int creatureID, int amount)
    {
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureID, out CreatureLogic creature))
        {
            pendingDamage.TryGetValue(creatureID, out int existingFallback);
            pendingDamage[creatureID] = existingFallback + amount;
            return;
        }

        bool wasAliveBefore = !IsEffectivelyDead(creature);
        pendingDamage.TryGetValue(creatureID, out int existing);
        pendingDamage[creatureID] = existing + amount;

        if (wasAliveBefore && IsEffectivelyDead(creature) && !creature.OnDeathResolvedInBattle)
        {
            Debug.Log($"[OnDeath:{zoneView.name}] Mort prédite en planification — {creature.DisplayName}(ID:{creature.UniqueCreatureID}) — résolution immédiate de OnDeath");
            creature.ResolvePredictedBattleDeath();
        }
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
