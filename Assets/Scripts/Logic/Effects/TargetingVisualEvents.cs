using System;
using System.Collections.Generic;

public static class TargetingVisualEvents
{
    public static event Action<List<PendingEffectSelection>, int> OnTargetingStarted;
    public static event Action                                    OnTargetingEnded;
    public static event Action OnEffectsExecuting;

    public static void RaiseTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
        => OnTargetingStarted?.Invoke(queue, currentIndex);

    public static void RaiseTargetingEnded()
        => OnTargetingEnded?.Invoke();

    public static void RaiseEffectsExecuting()
        => OnEffectsExecuting?.Invoke();
}

