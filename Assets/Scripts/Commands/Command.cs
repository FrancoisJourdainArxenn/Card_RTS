using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class Command
{
    public static Queue<Command> CommandQueue = new Queue<Command>();
    public static bool playingQueue = false;

    // Pendant la résolution anticipée d'un OnDeath en planification de combat (voir
    // CreatureLogic.ResolvePredictedBattleDeath), les Command qu'il déclenche sont stockées
    // ici au lieu d'être jouées immédiatement, pour être rejouées au bon moment : juste après
    // la CreatureDieCommand de la créature qui meurt (voir CreatureLogic.ScheduleBattleDeath).
    public static bool DeferForBattleReplay = false;
    private static readonly Dictionary<int, List<Action>> _deferredBySource = new();
    // Source explicite pour RunDeferred (appelants hors EffectRegistry.Execute, ex: rejeu réseau
    // d'un token — voir Player.NetworkSpawnTokenToZone) — prioritaire sur EffectRegistry.CurrentSourceID.
    private static int? _explicitDeferSourceID;

    public virtual void AddToQueue()
    {
        if (DeferForBattleReplay)
        {
            int sourceID = _explicitDeferSourceID ?? EffectRegistry.CurrentSourceID;
            Defer(sourceID, () => AddToQueue());
            return;
        }

        CommandQueue.Enqueue(this);
        // Debug.Log($"[Queue] Enqueue {GetType().Name} — taille file: {CommandQueue.Count} | playingQueue={playingQueue}");
        if (!playingQueue)
            PlayFirstCommandFromQueue();
    }

    public static void Defer(int sourceID, Action action)
    {
        if (!_deferredBySource.TryGetValue(sourceID, out List<Action> list))
            _deferredBySource[sourceID] = list = new List<Action>();
        list.Add(action);
    }

    // Pour les effets visuels qui NE passent PAS par une Command (ex: VfxManager.ShowDeathPending,
    // appelé directement depuis le setter Health) mais doivent quand même respecter le report en
    // cours : si DeferForBattleReplay est actif, `action` est mise de côté sous la même clé que les
    // Command (rejouée par FlushDeferredCommands) ; sinon elle s'exécute immédiatement comme avant.
    public static void DeferAction(Action action)
    {
        if (DeferForBattleReplay)
        {
            int sourceID = _explicitDeferSourceID ?? EffectRegistry.CurrentSourceID;
            Defer(sourceID, action);
        }
        else
        {
            action();
        }
    }

    // Exécute `action` avec le report actif, sous la clé `sourceID` explicite — pour les appelants
    // qui déclenchent des Command sans passer par EffectRegistry.Execute (donc sans
    // EffectRegistry.CurrentSourceID déjà positionné), ex: Player.NetworkSpawnTokenToZone rejouant
    // côté client un token dont la création provient d'un OnDeath résolu sur le serveur.
    public static void RunDeferred(int sourceID, Action action)
    {
        bool previousDefer = DeferForBattleReplay;
        int? previousSource = _explicitDeferSourceID;
        DeferForBattleReplay = true;
        _explicitDeferSourceID = sourceID;
        try
        {
            action();
        }
        finally
        {
            _explicitDeferSourceID = previousSource;
            DeferForBattleReplay = previousDefer;
        }
    }

    public static void FlushDeferredCommands(int sourceID)
    {
        if (!_deferredBySource.TryGetValue(sourceID, out List<Action> list)) return;
        _deferredBySource.Remove(sourceID);
        foreach (Action action in list)
            action();
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
        // Debug.Log($"[Queue] CommandExecutionComplete — restants: {CommandQueue.Count}");
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
        // Debug.Log($"[Queue] Démarre {next.GetType().Name} — restants après dequeue: {CommandQueue.Count}");
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
