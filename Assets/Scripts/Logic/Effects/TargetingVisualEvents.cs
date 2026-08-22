using System;
using System.Collections.Generic;

public static class TargetingVisualEvents
{
    public static event Action<List<PendingEffectSelection>, int> OnTargetingStarted;
    public static event Action                                    OnTargetingEnded;
    public static event Action OnEffectsExecuting;
    // Levé pour un effet auto isolé (sans sélection de cible) — voir RaiseEffectVisualCommand
    // et CardPreviewUI.HandleAutoEffect, qui est le seul abonné.
    // Partie 1 désactivée : plus aucun émetteur ni abonné.
    // public static event Action<CardEffectData, EffectContext> OnAutoEffectTriggered;
    public static event Action<int[], int[]> OnOpponentTargetingStarted;
    public static event Action               OnOpponentTargetingEnded;

    // Partie 3 (popup de batch de résolution de phase) désactivée : plus aucun émetteur ni abonné.
    // public static event Action<List<PendingEffectSelection>, List<bool>> OnEffectsBatchPending;
    // public static event Action                                            OnEffectResolved;
    // public static event Action                                            OnEffectsBatchComplete;

    public static void RaiseTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
        => OnTargetingStarted?.Invoke(queue, currentIndex);

    public static void RaiseTargetingEnded()
        => OnTargetingEnded?.Invoke();

    public static void RaiseEffectsExecuting()
        => OnEffectsExecuting?.Invoke();

    // Point d'entrée appelé par RaiseEffectVisualCommand ; invoque l'event seulement s'il y a
    // au moins un abonné (opérateur ?.).
    // Partie 1 désactivée.
    // public static void RaiseAutoEffectTriggered(CardEffectData data, EffectContext context)
    //     => OnAutoEffectTriggered?.Invoke(data, context);

    public static void RaiseOpponentTargetingStarted(int[] sourceEntityIDs, int[] effectIndexes)
        => OnOpponentTargetingStarted?.Invoke(sourceEntityIDs, effectIndexes);

    public static void RaiseOpponentTargetingEnded()
        => OnOpponentTargetingEnded?.Invoke();

    // Partie 3 désactivée.
    // public static void RaiseEffectsBatchPending(List<PendingEffectSelection> effects, List<bool> hasVisuals)
    //     => OnEffectsBatchPending?.Invoke(effects, hasVisuals);
    //
    // public static void RaiseEffectResolved()
    //     => OnEffectResolved?.Invoke();
    //
    // public static void RaiseEffectsBatchComplete()
    //     => OnEffectsBatchComplete?.Invoke();
}

