using System.Collections.Generic;
using UnityEngine;

// Statut Embuscade (lu par CondAmbush). Figé UNE fois par tour, à l'entrée en phase Battle — sur chaque
// machine, après résolution de tous les déplacements et débarquements (solo :
// TurnManager.ResolveStationaryTransportDisembarksThenEnterPhase, réseau :
// GameNetworkManager.ApplyDeathDrainAndTransition, qui attendent tous deux la fin de la file) — puis
// effacé au Regroup suivant. Figé plutôt que recalculé à chaque trigger, sinon il changerait en cours de
// tour (ennemis morts retirés au drain, base détruite retirée immédiatement, token invoqué compté comme
// allié déjà sur place...).
//
// Une créature est en embuscade si :
//   - elle est entrée CE round, par déplacement ou débarquement, dans la zone où elle se trouve ;
//   - la zone contient au moins une créature ennemie ou une base ennemie ;
//   - aucun allié n'y était déjà (créature présente avant les déplacements de ce round, ou base alliée).
//     Les alliés qui arrivent en même temps ne bloquent pas.
// Les créatures embarquées sont ignorées : leur BaseID n'est pas suivi pendant que le transport bouge,
// c'est le transport lui-même qui compte.
public static class AmbushTracker
{
    public static void Snapshot()
    {
        int round = TurnManager.Instance.CurrentRound;
        List<string> ambushing = new List<string>();
        foreach (Player p in Player.Players)
            foreach (CreatureLogic creature in p.playedCards.Creatures)
            {
                creature.IsAmbushing = ComputeAmbush(creature, round);
                if (creature.IsAmbushing)
                    ambushing.Add($"{creature.DisplayName}(ID:{creature.UniqueCreatureID})");
            }
        Debug.Log($"[Ambush] Snapshot round={round} — en embuscade : [{string.Join(", ", ambushing)}]");
        PassiveAuraManager.RecomputeAll();
    }

    public static void Clear()
    {
        foreach (Player p in Player.Players)
            foreach (CreatureLogic creature in p.playedCards.Creatures)
                creature.IsAmbushing = false;
        PassiveAuraManager.RecomputeAll();
    }

    static bool ComputeAmbush(CreatureLogic creature, int round)
    {
        if (creature.IsBoarded || creature.ZoneEnteredByMoveRound != round) return false;

        ZoneLogic zone = creature.Zone;
        if (zone == null || zone != creature.ZoneEnteredByMove) return false;

        return HasEnemyPresence(zone, creature.owner.otherPlayer)
            && !HasAllyAlreadyThere(zone, creature.owner, round);
    }

    static bool HasEnemyPresence(ZoneLogic zone, Player enemy)
    {
        foreach (CreatureLogic c in enemy.playedCards.Creatures)
            if (!c.IsBoarded && c.Zone == zone) return true;

        foreach (BaseLogic b in BaseLogic.BasesCreatedThisGame.Values)
            if (b.owner == enemy && b.Zone == zone) return true;

        return false;
    }

    static bool HasAllyAlreadyThere(ZoneLogic zone, Player ally, int round)
    {
        foreach (CreatureLogic c in ally.playedCards.Creatures)
            if (!c.IsBoarded && c.Zone == zone && c.ZoneEnteredByMoveRound != round) return true;

        foreach (BaseLogic b in BaseLogic.BasesCreatedThisGame.Values)
            if (b.owner == ally && b.Zone == zone) return true;

        return false;
    }
}
