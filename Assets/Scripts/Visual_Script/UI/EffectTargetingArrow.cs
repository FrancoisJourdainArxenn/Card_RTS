using System.Collections.Generic;
using UnityEngine;

public class EffectTargetingArrow : MonoBehaviour
{
    [SerializeField] private HoverArrow arrow;

    void OnEnable()
    {
        Debug.Log("[EffectTargetingArrow] OnEnable — abonnement aux events");

        TargetingVisualEvents.OnTargetingStarted += OnTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded   += OnTargetingEnded;
    }

    void OnDisable()
    {
        TargetingVisualEvents.OnTargetingStarted -= OnTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded   -= OnTargetingEnded;
    }

    private void OnTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
    {
        GameObject sourceObject = IDHolder.GetGameObjectWithID(queue[currentIndex].SourceEntityID);
        Debug.Log($"[EffectTargetingArrow] sourceObject = {sourceObject}");

        if (sourceObject == null) return;

        arrow.ShowToMouse();
    }

    private void OnTargetingEnded() => arrow.Hide();
}
