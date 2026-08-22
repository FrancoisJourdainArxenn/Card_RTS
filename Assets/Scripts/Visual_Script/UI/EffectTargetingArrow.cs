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
        PendingEffectSelection current = queue[currentIndex];

        // SourceEntityID < 0 : ciblage OnPlay avant que la créature n'existe (voir
        // OnPlayTargetingSession) — pas d'entité source à vérifier, la flèche suit simplement la souris.
        GameObject sourceGO = current.SourceEntityID >= 0 ? IDHolder.GetGameObjectWithID(current.SourceEntityID) : null;
        if (current.SourceEntityID >= 0 && sourceGO == null)
            return;

        StopAllCoroutines();

        if (current.SelectedTarget != null)
            arrow.Hide();
        else
        {
            // Ancre la flèche sur la vraie source (ghost de créature, ou carte de sort en main)
            // plutôt que de la laisser à sa position précédente/par défaut dans la scène.
            if (sourceGO != null)
                arrow.transform.position = sourceGO.transform.position;
            StartCoroutine(ShowArrowDelayed());
        }
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
