using System.Collections.Generic;
using UnityEngine;

public static class CommandMoveTracker
{
    private struct MoveRecord
    {
        public ZoneLogic fromZone;
        public ZoneLogic toZone;
        public Player player;
    }

    private static readonly List<MoveRecord> moves = new List<MoveRecord>();

    public static void Clear()
    {
        moves.Clear();
    }

    public static void RegisterMove(ZoneLogic fromZone, ZoneLogic toZone, Player player)
    {
        if (fromZone == null || toZone == null) return;

        foreach (var move in moves)
        {
            if (move.fromZone == toZone && move.toZone == fromZone && move.player != player)
            {
                // Debug.Log($"[Crossing] {player} moves {fromZone.DisplayName}→{toZone.DisplayName} crosses {move.player} moving {move.fromZone.DisplayName}→{move.toZone.DisplayName}");
            }
        }
        moves.Add(new MoveRecord { fromZone = fromZone, toZone = toZone, player = player });
    }
}
