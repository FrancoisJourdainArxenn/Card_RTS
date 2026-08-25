using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Flèche-pointeur dédiée exclusivement au ciblage d'un sort en train d'être drag depuis la main
// (voir DragSpellOnTarget) — n'a pas de position de repos fixe à préserver comme EffectTargetingArrow :
// elle n'existe que le temps du drag, donc elle peut toujours partir de la carte source.
public class SpellTargetingArrow : MonoBehaviour
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
        PendingEffectSelection current = queue[currentIndex];

        StopAllCoroutines();

        if (!current.IsSpellTargeting || current.SelectedTarget != null)
        {
            arrow.Hide();
            return;
        }

        GameObject sourceGO = IDHolder.GetGameObjectWithID(current.SourceEntityID);
        if (sourceGO == null)
        {
            arrow.Hide();
            return;
        }

        arrow.transform.position = sourceGO.transform.position;
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
