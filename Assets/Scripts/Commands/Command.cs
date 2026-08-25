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
    // Bucket séparé pour les commandes de mort (CreatureDieCommand/BuildingDieCommand) — toujours
    // rejoué APRÈS _deferredBySource pour la même clé (voir FlushDeferredCommands), pour qu'une mort
    // ne soit jamais rejouée avant les commandes "cause" qui doivent la précéder visuellement.
    private static readonly Dictionary<int, List<Action>> _deferredDeathsBySource = new();
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

        AddToQueueImmediate();
    }

    // Enqueue réel, sans passer par le contrôle DeferForBattleReplay — utilisé par les actions déjà
    // explicitement reportées (voir DeferDeath) au moment où elles rejouent : à ce stade on veut
    // l'enqueue direct, pas un nouveau report.
    public void AddToQueueImmediate()
    {
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

    // Comme Defer(), mais dans le bucket "morts" — voir _deferredDeathsBySource.
    public static void DeferDeath(int sourceID, Action action)
    {
        if (!_deferredDeathsBySource.TryGetValue(sourceID, out List<Action> list))
            _deferredDeathsBySource[sourceID] = list = new List<Action>();
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
        // Le bucket "causes" est TOUJOURS rejoué avant le bucket "morts" pour la même clé, quel que
        // soit l'ordre dans lequel Defer()/DeferDeath() ont été appelés — sinon une CreatureDieCommand
        // pourrait rejouer avant la commande censée la précéder (voir CreatureLogic.MarkPendingDeath).
        if (_deferredBySource.TryGetValue(sourceID, out List<Action> causeList))
        {
            _deferredBySource.Remove(sourceID);
            foreach (Action action in causeList)
                action();
        }
        if (_deferredDeathsBySource.TryGetValue(sourceID, out List<Action> deathList))
        {
            _deferredDeathsBySource.Remove(sourceID);
            foreach (Action action in deathList)
                action();
        }
    }

    // Permet à un appelant (ex: ZoneCombatResolver.EnqueueBattleCommands) de savoir s'il y a
    // quelque chose à révéler pour cette clé avant de décider d'attendre la caméra ou non.
    public static bool HasDeferredCommands(int sourceID) =>
        _deferredBySource.ContainsKey(sourceID) || _deferredDeathsBySource.ContainsKey(sourceID);

    // Clé de report actuellement active (celle passée au RunDeferred englobant), ou null si on
    // n'est pas dans un tel contexte. Utilisé par TokenGenerationSO.Execute pour transmettre la
    // BONNE clé à travers le réseau (BroadCastTokenToZone → NetworkSpawnTokenToZone) — sans ça,
    // le client ne peut pas savoir sous quelle clé re-différer le spawn du token : la clé dépend
    // du trigger d'origine (ID de créature pour OnDeath, zoneDeferKey pour OnBattleStart, etc.),
    // pas d'une seule convention fixe.
    public static int? CurrentDeferSourceID =>
        DeferForBattleReplay ? (_explicitDeferSourceID ?? EffectRegistry.CurrentSourceID) : (int?)null;

    // Point d'entrée unique pour transformer les morts logiques accumulées (CreatureLogic/BuildingLogic)
    // en commandes visuelles. À appeler explicitement par l'orchestrateur (EffectRegistry.Execute,
    // ZoneCombatResolver.EnqueueBattleCommands, ...) juste APRÈS avoir mis en file ses propres commandes
    // "cause" — jamais automatiquement depuis le setter Health, pour garantir que la CreatureDieCommand/
    // BuildingDieCommand d'une cible n'entre jamais dans la file avant l'animation censée la tuer.
    public static void FlushPendingDeaths()
    {
        CreatureLogic.QueuePendingDeathVisuals();
        BuildingLogic.QueuePendingDeathVisuals();
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
        // Debug.Log($"[DBG][Timing] COMPLETE @ {Time.realtimeSinceStartup:F3} — restants: {CommandQueue.Count}");
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
        // Debug.Log($"[DBG][Timing] START {next.GetType().Name} @ {Time.realtimeSinceStartup:F3}");
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
