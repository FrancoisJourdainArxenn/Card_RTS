using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class ZoneClashMoveCommand : Command
{
    private List<(int creatureID, Vector3 targetPos)> moves;
    private float duration;

    public ZoneClashMoveCommand(List<(int creatureID, Vector3 targetPos)> moves, float duration)
    {
        this.moves = moves;
        this.duration = duration;
    }

    public override void StartCommandExecution()
    {
        int remaining = moves.Count;
        if (remaining == 0) { CommandExecutionComplete(); return; }

        foreach (var (id, pos) in moves)
        {
            GameObject go = IDHolder.GetGameObjectWithID(id);
            if (go == null) { if (--remaining == 0) CommandExecutionComplete(); continue; }
            go.transform.DOKill();
            go.transform.DOMove(pos, duration).OnComplete(() =>
            {
                if (--remaining == 0) CommandExecutionComplete();
            });
        }
    }
}
