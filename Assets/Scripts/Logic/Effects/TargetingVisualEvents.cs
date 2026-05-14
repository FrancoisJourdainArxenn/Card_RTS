using System;
using System.Collections.Generic;

public static class TargetingVisualEvents
{
    public static event Action<List<PendingEffectSelection>, int> OnTargetingStarted;
    public static event Action                                    OnTargetingEnded;
    public static event Action OnEffectsExecuting;
    public static event Action<CardEffectData, EffectContext> OnAutoEffectTriggered;
    public static event Action<int[], int[]> OnOpponentTargetingStarted;
    public static event Action               OnOpponentTargetingEnded;

    public static event Action<List<PendingEffectSelection>, List<bool>> OnEffectsBatchPending;
    public static event Action                                            OnEffectResolved;
    public static event Action                                            OnEffectsBatchComplete;

    public static void RaiseTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
        => OnTargetingStarted?.Invoke(queue, currentIndex);

    public static void RaiseTargetingEnded()
        => OnTargetingEnded?.Invoke();

    public static void RaiseEffectsExecuting()
        => OnEffectsExecuting?.Invoke();

    public static void RaiseAutoEffectTriggered(CardEffectData data, EffectContext context)
        => OnAutoEffectTriggered?.Invoke(data, context);

    public static void RaiseOpponentTargetingStarted(int[] sourceEntityIDs, int[] effectIndexes)
        => OnOpponentTargetingStarted?.Invoke(sourceEntityIDs, effectIndexes);

    public static void RaiseOpponentTargetingEnded()
        => OnOpponentTargetingEnded?.Invoke();

    public static void RaiseEffectsBatchPending(List<PendingEffectSelection> effects, List<bool> hasVisuals)
        => OnEffectsBatchPending?.Invoke(effects, hasVisuals);

    public static void RaiseEffectResolved()
        => OnEffectResolved?.Invoke();

    public static void RaiseEffectsBatchComplete()
        => OnEffectsBatchComplete?.Invoke();
}

