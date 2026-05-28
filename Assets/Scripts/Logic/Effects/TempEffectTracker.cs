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

    /// <summary>Fires and removes all revert actions for every entity. Call at start of Regroup.</summary>
    public static void RevertAll()
    {
        foreach (List<Action> actions in _revertActions.Values)
            foreach (Action action in actions)
                action.Invoke();

        _revertActions.Clear();
    }

    /// <summary>Removes all pending revert actions for an entity (e.g. on death).</summary>
    public static void Unregister(int entityId)
    {
        _revertActions.Remove(entityId);
    }

    public static void Reset() => _revertActions.Clear();
}
