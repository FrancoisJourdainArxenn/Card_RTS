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

        // Le ciblage de sort a sa propre flèche dédiée (voir SpellTargetingArrow) : on ne touche
        // pas à celle-ci pour ne pas afficher les deux en même temps.
        if (current.IsSpellTargeting)
        {
            StopAllCoroutines();
            arrow.Hide();
            return;
        }

        // SourceEntityID < 0 : ciblage OnPlay avant que la créature n'existe (voir
        // OnPlayTargetingSession) — pas d'entité source à vérifier, la flèche suit simplement la souris.
        if (current.SourceEntityID >= 0 && IDHolder.GetGameObjectWithID(current.SourceEntityID) == null)
            return;

        StopAllCoroutines();

        // Point de départ fixe dans la scène (voir Effect_TargetingArrow) : cette flèche ne suit
        // aucune source, seule sa pointe suit la souris.
        if (current.SelectedTarget != null)
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
