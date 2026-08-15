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
    // Actions de mort (CreatureDieCommand) mises à part de _deferredBySource et rejouées APRÈS
    // celui-ci pour la même clé (voir FlushDeferredCommands) — voir CreatureLogic.MarkPendingDeath.
    private static readonly Dictionary<int, List<Action>> _deferredDeathsBySource = new();
    // Source explicite pour RunDeferred (appelants hors EffectRegistry.Execute, ex: rejeu réseau
    // d'un token — voir Player.NetworkSpawnTokenToZone) — prioritaire sur EffectRegistry.CurrentSourceID.
    private static int? _explicitDeferSourceID;

    public virtual void AddToQueue()
    {
        if (DeferForBattleReplay)
        {
            int sourceID = _explicitDeferSourceID ?? EffectRegistry.CurrentSourceID;
            Defer(sourceID, AddToQueueImmediate);
            return;
        }

        AddToQueueImmediate();
    }

    // Corps non différé d'AddToQueue — extrait pour que CreatureLogic.MarkPendingDeath puisse
    // enqueue la CreatureDieCommand directement une fois qu'elle a déjà décidé, elle-même, de la
    // reporter via DeferDeath plutôt que via le report générique (voir DeferDeath ci-dessous).
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

    // Report spécifique à la mort d'une créature (voir CreatureLogic.MarkPendingDeath) : une action
    // normale (dégâts, projectile, popup...) et la mort qu'elle cause peuvent toutes deux être
    // résolues pendant la MÊME résolution différée (ex: Sniper — On Battle Start: 2 dégâts à une
    // cible aléatoire — tue sa cible avant même que le DealDamageCommand n'ait été mis en file,
    // puisque TakeDamage/MarkPendingDeath s'exécute AVANT que l'appelant n'enqueue sa propre
    // commande). Sans séparation, la mort se retrouve avant sa cause dans _deferredBySource, dans
    // l'ordre où le code les a exécutées — la créature "meurt" à l'écran avant que le missile qui
    // la tue n'ait eu la chance de partir. En gardant les morts dans une liste à part, rejouée
    // seulement APRÈS la liste normale de la même clé (voir FlushDeferredCommands), la cause joue
    // toujours avant sa conséquence, quel que soit l'ordre d'exécution du code qui les a émises.
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
        if (_deferredBySource.TryGetValue(sourceID, out List<Action> list))
        {
            _deferredBySource.Remove(sourceID);
            foreach (Action action in list)
                action();
        }

        // Les morts de cette même clé rejouent APRÈS leurs causes ci-dessus — voir DeferDeath.
        if (_deferredDeathsBySource.TryGetValue(sourceID, out List<Action> deaths))
        {
            _deferredDeathsBySource.Remove(sourceID);
            foreach (Action action in deaths)
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
