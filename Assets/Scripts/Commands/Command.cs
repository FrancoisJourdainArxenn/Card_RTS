using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Command
{
    public static Queue<Command> CommandQueue = new Queue<Command>();
    public static bool playingQueue = false;

    public virtual void AddToQueue()
    {
        CommandQueue.Enqueue(this);
        Debug.Log($"[Queue] Enqueue {GetType().Name} — taille file: {CommandQueue.Count} | playingQueue={playingQueue}");
        if (!playingQueue)
            PlayFirstCommandFromQueue();
    }

    public virtual void StartCommandExecution()
    {
        // list of everything that we have to do with this command (draw a card, play a card, play spell effect, etc...)
        // there are 2 options of timing :
        // 1) use tween sequences and call CommandExecutionComplete in OnComplete()
        // 2) use coroutines (IEnumerator) and WaitFor... to introduce delays, call CommandExecutionComplete() in the end of coroutine
    }

    public static void CommandExecutionComplete()
    {
        Debug.Log($"[Queue] CommandExecutionComplete — restants: {CommandQueue.Count}");
        if (CommandQueue.Count > 0)
            PlayFirstCommandFromQueue();
        else
            playingQueue = false;
        if (!EffectSelectionController.HasPendingSelection)
            TurnManager.RefreshAllPlayableHighlights();
        EffectSelectionController.RefreshCurrentHighlights();
        ZoneEnemyIndicator.RefreshAll();
        PathVisual.RefreshAll();
    }

    public static void PlayFirstCommandFromQueue()
    {
        playingQueue = true;
        Command next = CommandQueue.Dequeue();
        Debug.Log($"[Queue] Démarre {next.GetType().Name} — restants après dequeue: {CommandQueue.Count}");
        next.StartCommandExecution();
    }

    public static bool CardDrawPending()
    {
        foreach (Command c in CommandQueue)
        {
            if (c is DrawACardCommand)
                return true;
        }
        return false;
    }
}
