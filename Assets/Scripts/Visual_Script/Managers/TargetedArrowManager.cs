using System.Collections.Generic;
using UnityEngine;

public class TargetedArrowManager : MonoBehaviour
{
    [SerializeField] private HoverArrow arrowPrefab;

    private readonly Dictionary<int, HoverArrow> _arrowsByIndex = new();

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
        if (current.SelectedTarget == null) return;

        // Remplace l'éventuelle flèche existante pour cet index (si le joueur change de cible)
        if (_arrowsByIndex.TryGetValue(currentIndex, out HoverArrow existing) && existing != null)
        {
            existing.Hide();
            Destroy(existing.gameObject);
        }

        GameObject sourceGO = IDHolder.GetGameObjectWithID(current.SourceEntityID);
        GameObject targetGO = IDHolder.GetGameObjectWithID(current.SelectedTarget.ID);
        if (sourceGO == null || targetGO == null) return;

        HoverArrow arrow = Instantiate(arrowPrefab);
        arrow.Show(GetAnchor(sourceGO), GetAnchor(targetGO));

        _arrowsByIndex[currentIndex] = arrow;
    }

    private void OnTargetingEnded()
    {
        foreach (HoverArrow arrow in _arrowsByIndex.Values)
            if (arrow != null) { arrow.Hide(); Destroy(arrow.gameObject); }
        _arrowsByIndex.Clear();
    }

    private static Transform GetAnchor(GameObject go)
    {
        Transform center = go.transform.Find("CenterPoint");
        return center != null ? center : go.transform;
    }

}
