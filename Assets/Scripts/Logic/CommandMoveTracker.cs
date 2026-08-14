using System.Collections.Generic;
using UnityEngine;

public static class CommandMoveTracker
{
    public struct PendingMoveInfo
    {
        public int creatureID;
        public int originBaseID;
        public int targetBaseID;
        public int tablePos;
        public Player owner;
    }

    /// <summary>Ordre (IDs permanents) à appliquer sur la TableVisual d'un baseID donné.</summary>
    public struct CrossingOrderUpdate
    {
        public Player owner;
        public int baseID;
        public int[] meleeIDs;
        public int[] rangedIDs;
    }

    public class CrossingRedirectResult
    {
        public readonly Dictionary<int, int> Redirects = new();
        public readonly List<CrossingOrderUpdate> OrderUpdates = new();
    }

    public class CrossingDispatchResult
    {
        public readonly List<(int creatureID, int baseID)> Relocations = new();
        public readonly List<CrossingOrderUpdate> OrderUpdates = new();
    }

    /// <summary>
    /// À appeler avant que les mouvements en attente ne s'exécutent réellement (avant tout Move()).
    /// Détecte les croisements (mouvements opposés, joueurs différents, même paire de zones),
    /// redirige les créatures concernées vers un CrossingZoneSlot libre et calcule l'ordre
    /// d'arrivée à y appliquer (préservant l'ordre connu de leur vraie destination d'origine).
    /// </summary>
    public static CrossingRedirectResult ResolveCrossings(List<PendingMoveInfo> pendingMoves)
    {
        CrossingRedirectResult result = new CrossingRedirectResult();

        Dictionary<int, List<PendingMoveInfo>> byPair = new Dictionary<int, List<PendingMoveInfo>>();
        Dictionary<int, ZoneLogic> pairFirstZone = new Dictionary<int, ZoneLogic>();

        foreach (PendingMoveInfo move in pendingMoves)
        {
            if (move.owner == null) continue;
            ZoneLogic originZone = move.owner.GetPlayerAreaByID(move.originBaseID)?.parentZone?.Logic;
            ZoneLogic targetZone = move.owner.GetPlayerAreaByID(move.targetBaseID)?.parentZone?.Logic;
            if (originZone == null || targetZone == null || originZone == targetZone)
                continue;

            int key = originZone.ID ^ targetZone.ID;
            if (!byPair.TryGetValue(key, out List<PendingMoveInfo> list))
            {
                list = new List<PendingMoveInfo>();
                byPair[key] = list;
                pairFirstZone[key] = originZone;
            }
            list.Add(move);
        }

        foreach (KeyValuePair<int, List<PendingMoveInfo>> kvp in byPair)
        {
            ZoneLogic zoneA = pairFirstZone[kvp.Key];
            List<PendingMoveInfo> sideA = new List<PendingMoveInfo>(); // zoneA -> zoneB
            List<PendingMoveInfo> sideB = new List<PendingMoveInfo>(); // zoneB -> zoneA

            foreach (PendingMoveInfo move in kvp.Value)
            {
                ZoneLogic originZone = move.owner.GetPlayerAreaByID(move.originBaseID).parentZone.Logic;
                (originZone == zoneA ? sideA : sideB).Add(move);
            }

            if (sideA.Count == 0 || sideB.Count == 0)
                continue;

            bool isCrossing = false;
            foreach (PendingMoveInfo a in sideA)
            {
                foreach (PendingMoveInfo b in sideB)
                {
                    if (a.owner != b.owner) { isCrossing = true; break; }
                }
                if (isCrossing) break;
            }
            if (!isCrossing) continue;

            CrossingZoneSlot slot = CrossingZoneSlot.GetFreeSlot();
            if (slot == null)
            {
                Debug.LogWarning("[CommandMoveTracker] Croisement détecté mais aucun CrossingZoneSlot libre — mouvement non redirigé.");
                continue;
            }
            slot.MarkInUse();

            // Un même côté peut en théorie contenir les deux joueurs (ex: les deux avancent dans le
            // même sens pendant qu'un troisième groupe croise en sens inverse) — on regroupe donc par
            // propriétaire avant de rediriger, plutôt que de supposer un seul joueur par côté.
            RedirectSideByOwner(sideA, slot, result);
            RedirectSideByOwner(sideB, slot, result);
        }

        return result;
    }

    private static void RedirectSideByOwner(List<PendingMoveInfo> side, CrossingZoneSlot slot, CrossingRedirectResult result)
    {
        Dictionary<Player, List<PendingMoveInfo>> byOwner = new Dictionary<Player, List<PendingMoveInfo>>();
        foreach (PendingMoveInfo move in side)
        {
            if (!byOwner.TryGetValue(move.owner, out List<PendingMoveInfo> list))
            {
                list = new List<PendingMoveInfo>();
                byOwner[move.owner] = list;
            }
            list.Add(move);
        }

        foreach (List<PendingMoveInfo> ownerMoves in byOwner.Values)
            RedirectOwnerGroup(ownerMoves, slot, result);
    }

    private static void RedirectOwnerGroup(List<PendingMoveInfo> ownerMoves, CrossingZoneSlot slot, CrossingRedirectResult result)
    {
        Player owner = ownerMoves[0].owner;
        PlayerArea slotArea = FindAreaForOwner(slot.Zone, owner);
        if (slotArea == null) return;

        // La destination d'origine (avant redirection) porte déjà, en temps réel, l'ordre que le
        // joueur a construit en glissant ses fantômes de déplacement (TableVisual.ApplyCreatureOrder).
        PlayerArea originalDestArea = owner.GetPlayerAreaByID(ownerMoves[0].targetBaseID);
        TableVisual originalTable = originalDestArea?.tableVisual;

        HashSet<int> movingIDs = new HashSet<int>();
        foreach (PendingMoveInfo move in ownerMoves) movingIDs.Add(move.creatureID);

        int[] meleeIDs = FilterKnownOrder(originalTable?.LastKnownMeleeOrder, movingIDs, ownerMoves, true);
        int[] rangedIDs = FilterKnownOrder(originalTable?.LastKnownRangedOrder, movingIDs, ownerMoves, false);

        slotArea.tableVisual.ApplyCreatureOrder(meleeIDs, rangedIDs);
        result.OrderUpdates.Add(new CrossingOrderUpdate { owner = owner, baseID = slotArea.baseID, meleeIDs = meleeIDs, rangedIDs = rangedIDs });

        Debug.Log($"[Crossing] Redirect {owner.name} — créatures=[{string.Join(",", ownerMoves.ConvertAll(m => m.creatureID))}] slotBaseID={slotArea.baseID} (origine du groupe={ownerMoves[0].originBaseID}, destination d'origine={ownerMoves[0].targetBaseID}) meleeIDs=[{string.Join(",", meleeIDs)}] rangedIDs=[{string.Join(",", rangedIDs)}]");

        foreach (PendingMoveInfo move in ownerMoves)
        {
            result.Redirects[move.creatureID] = slotArea.baseID;
            slot.RecordOriginAndDestination(move.owner, move.originBaseID, move.targetBaseID);
        }
    }

    /// <summary>
    /// Filtre un ordre connu (IDs permanents) aux seuls IDs de movingIDs, dans leur ordre relatif.
    /// Les IDs de movingIDs absents de l'ordre connu (jamais reorder pendant le drag) sont ajoutés
    /// à la fin, dans l'ordre où ils apparaissent dans ownerMoves, en respectant mêlée/distance.
    /// </summary>
    private static int[] FilterKnownOrder(IReadOnlyList<int> knownOrder, HashSet<int> movingIDs, List<PendingMoveInfo> ownerMoves, bool melee)
    {
        List<int> filtered = new List<int>();
        HashSet<int> seen = new HashSet<int>();

        if (knownOrder != null)
        {
            foreach (int id in knownOrder)
            {
                if (!movingIDs.Contains(id) || !IsMeleeCreature(id, melee)) continue;
                filtered.Add(id);
                seen.Add(id);
            }
        }

        foreach (PendingMoveInfo move in ownerMoves)
        {
            if (seen.Contains(move.creatureID) || !IsMeleeCreature(move.creatureID, melee)) continue;
            filtered.Add(move.creatureID);
            seen.Add(move.creatureID);
        }

        return filtered.ToArray();
    }

    private static bool IsMeleeCreature(int creatureID, bool melee)
        => CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureID, out CreatureLogic c) && c.IsMelee == melee;

    private static PlayerArea FindAreaForOwner(ZoneManager zone, Player owner)
    {
        AreaPosition side = owner == GlobalSettings.Instance.LowPlayer ? AreaPosition.Low : AreaPosition.Top;
        foreach (PlayerArea pa in zone.subZones)
            if (pa.owner == side)
                return pa;
        return null;
    }

    /// <summary>
    /// À appeler une fois le combat de croisement résolu et les morts de cette phase traitées
    /// (donc l'état de survie de chaque camp est fiable). Détermine, pour chaque slot occupé,
    /// où renvoyer les survivants selon la règle : un seul camp survit → il continue vers sa
    /// destination d'origine ; les deux survivent → chacun retourne vers son origine ; personne
    /// ne survit → rien à faire. Calcule aussi l'ordre à appliquer sur la table d'arrivée, à
    /// partir de l'ordre actuel dans la zone de croisement. Libère chaque slot traité.
    /// Pur calcul, aucun effet de bord sur les créatures elles-mêmes — voir ApplyCrossingDispatch.
    /// </summary>
    public static CrossingDispatchResult ComputeCrossingDispatch()
    {
        CrossingDispatchResult result = new CrossingDispatchResult();
        List<CrossingZoneSlot> activeSlots = new List<CrossingZoneSlot>();
        foreach (CrossingZoneSlot slot in CrossingZoneSlot.AllSlots)
            if (slot.InUse)
                activeSlots.Add(slot);

        Player top = GlobalSettings.Instance.TopPlayer;
        Player low = GlobalSettings.Instance.LowPlayer;

        foreach (CrossingZoneSlot slot in activeSlots)
        {
            bool topHasMemory = slot.TryGetMemory(top, out int topOrigin, out int topDest);
            bool lowHasMemory = slot.TryGetMemory(low, out int lowOrigin, out int lowDest);
            bool topSurvives = topHasMemory && HasSurvivors(top, slot);
            bool lowSurvives = lowHasMemory && HasSurvivors(low, slot);

            if (topSurvives && lowSurvives)
            {
                CollectRelocations(top, slot, topOrigin, result);
                CollectRelocations(low, slot, lowOrigin, result);
            }
            else if (topSurvives)
            {
                CollectRelocations(top, slot, topDest, result);
            }
            else if (lowSurvives)
            {
                CollectRelocations(low, slot, lowDest, result);
            }

            slot.MarkFree();
        }

        return result;
    }

    /// <summary>Applique un dispatch déjà calculé (par ComputeCrossingDispatch) : ordre puis relocalisation.</summary>
    public static void ApplyCrossingDispatch(CrossingDispatchResult dispatch)
    {
        foreach (CrossingOrderUpdate update in dispatch.OrderUpdates)
        {
            PlayerArea area = update.owner.GetPlayerAreaByID(update.baseID);
            area?.tableVisual.ApplyCreatureOrder(update.meleeIDs, update.rangedIDs);
        }

        foreach ((int creatureID, int baseID) in dispatch.Relocations)
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureID, out CreatureLogic creature))
                creature.RelocateAfterCombat(baseID, 0);
    }

    private static bool HasSurvivors(Player player, CrossingZoneSlot slot)
    {
        PlayerArea area = FindAreaForOwner(slot.Zone, player);
        if (area == null) return false;
        foreach (CreatureLogic c in player.playedCards.Creatures)
            if (c.BaseID == area.baseID)
                return true;
        return false;
    }

    private static void CollectRelocations(Player player, CrossingZoneSlot slot, int destinationBaseID, CrossingDispatchResult result)
    {
        PlayerArea area = FindAreaForOwner(slot.Zone, player);
        if (area == null) return;

        List<int> survivorIDs = new List<int>();
        foreach (CreatureLogic c in player.playedCards.Creatures)
            if (c.BaseID == area.baseID)
                survivorIDs.Add(c.UniqueCreatureID);
        if (survivorIDs.Count == 0) return;

        HashSet<int> survivorSet = new HashSet<int>(survivorIDs);
        int[] meleeIDs = FilterKnownOrderByID(area.tableVisual.LastKnownMeleeOrder, survivorSet, survivorIDs, true);
        int[] rangedIDs = FilterKnownOrderByID(area.tableVisual.LastKnownRangedOrder, survivorSet, survivorIDs, false);
        result.OrderUpdates.Add(new CrossingOrderUpdate { owner = player, baseID = destinationBaseID, meleeIDs = meleeIDs, rangedIDs = rangedIDs });

        Debug.Log($"[Crossing] Dispatch {player.name} — survivants=[{string.Join(",", survivorIDs)}] slotBaseID={area.baseID} → destinationBaseID={destinationBaseID} meleeIDs=[{string.Join(",", meleeIDs)}] rangedIDs=[{string.Join(",", rangedIDs)}]");

        foreach (int id in survivorIDs)
            result.Relocations.Add((id, destinationBaseID));
    }

    private static int[] FilterKnownOrderByID(IReadOnlyList<int> knownOrder, HashSet<int> keepIDs, List<int> fallbackOrder, bool melee)
    {
        List<int> filtered = new List<int>();
        HashSet<int> seen = new HashSet<int>();

        if (knownOrder != null)
        {
            foreach (int id in knownOrder)
            {
                if (!keepIDs.Contains(id) || !IsMeleeCreature(id, melee)) continue;
                filtered.Add(id);
                seen.Add(id);
            }
        }

        foreach (int id in fallbackOrder)
        {
            if (seen.Contains(id) || !IsMeleeCreature(id, melee)) continue;
            filtered.Add(id);
            seen.Add(id);
        }

        return filtered.ToArray();
    }
}
