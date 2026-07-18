using System;
using System.Collections.Generic;

/// <summary>
/// Tracks revert actions for "until end of turn" effects.
/// Each EffectSO that supports UntilEndOfTurn registers a delegate here after applying;
/// that delegate is called at the start of the next Regroup phase to undo the effect.
/// </summary>
public static class TempEffectTracker
{
    private static readonly Dictionary<int, List<Action>> _revertActions = new();

    /// <summary>Registers a revert action for an entity (by unique ID).</summary>
    public static void Register(int entityId, Action revert)
    {
        if (!_revertActions.ContainsKey(entityId))
            _revertActions[entityId] = new List<Action>();

        _revertActions[entityId].Add(revert);
    }

    /// <summary>Fires and removes all revert actions for every entity. Call at start of Regroop.</summary>
    // Fix 2026-07-16: snapshot the values then clear _revertActions BEFORE invoking the revert
    // actions. A revert action can have side effects (e.g. a creature dying) that call
    // Register/Unregister on _revertActions; doing that while still enumerating .Values threw
    // "InvalidOperationException: Collection was modified", which aborted EnterPhase(Regroup)
    // partway through and left clients permanently stuck in the Regroup phase.
    public static void RevertAll()
    {
        List<List<Action>> snapshot = new List<List<Action>>(_revertActions.Values);
        _revertActions.Clear();

        foreach (List<Action> actions in snapshot)
            foreach (Action action in actions)
                action.Invoke();
    }

    /// <summary>Removes all pending revert actions for an entity (e.g. on death).</summary>
    public static void Unregister(int entityId)
    {
        _revertActions.Remove(entityId);
    }

    public static void Reset() => _revertActions.Clear();
}
