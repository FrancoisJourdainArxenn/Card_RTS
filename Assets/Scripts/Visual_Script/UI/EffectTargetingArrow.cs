using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EffectTargetingArrow : MonoBehaviour
{
    [SerializeField] private HoverArrow arrow;

    void OnEnable()
    {
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
        if (sourceObject == null) return;

        StopAllCoroutines();

        if (queue[currentIndex].SelectedTarget != null)
            arrow.Hide();
        else
            StartCoroutine(ShowArrowDelayed());
    }

    private IEnumerator ShowArrowDelayed()
    {
        float delay = CardPreviewUI.Instance != null ? CardPreviewUI.Instance.StackAppearDelay : 0f;
        yield return new WaitForSeconds(delay);
        arrow.ShowToMouse();
    }

    private void OnTargetingEnded()
    {
        StopAllCoroutines();
        arrow.Hide();
    }
}
