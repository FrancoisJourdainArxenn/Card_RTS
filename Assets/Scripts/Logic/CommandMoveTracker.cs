using System.Collections.Generic;
using UnityEngine;

public static class CommandMoveTracker
{
    private struct MoveRecord
    {
        public int fromBase;
        public int toBase;
        public Player player;
    }

    private static readonly List<MoveRecord> moves = new List<MoveRecord>();

    public static void Clear()
    {
        moves.Clear();
    }

    public static void RegisterMove(int fromBase, int toBase, Player player)
    {
        foreach (var move in moves)
        {
            if (move.fromBase == toBase && move.toBase == fromBase && move.player != player)
            {
                Debug.Log($"[Crossing] {player} moves {fromBase}→{toBase} crosses {move.player} moving {move.fromBase}→{move.toBase}");
            }
        }
        moves.Add(new MoveRecord { fromBase = fromBase, toBase = toBase, player = player });
    }
}
