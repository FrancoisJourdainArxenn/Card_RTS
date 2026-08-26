using DG.Tweening;
using UnityEngine;

public static class ValuePopAnimation
{
    public static void Pop(Transform t)
    {
        float strength = VisualManager.Instance != null ? VisualManager.Instance.popStrength : 0.35f;
        float duration = VisualManager.Instance != null ? VisualManager.Instance.popDuration : 0.35f;
        t.DOKill();
        t.DOPunchScale(t.localScale * strength, duration, 1, 0.5f);
    }
}
