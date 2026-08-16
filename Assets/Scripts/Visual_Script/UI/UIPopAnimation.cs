using DG.Tweening;
using UnityEngine;

public static class UIPopAnimation
{
    public static void Pop(Transform t)
    {
        float strength = VisualManager.Instance != null ? VisualManager.Instance.popStrength : 0.35f;
        float duration = VisualManager.Instance != null ? VisualManager.Instance.popDuration : 0.35f;
        t.DOKill();
        t.localScale = Vector3.one;
        t.DOPunchScale(Vector3.one * strength, duration, 1, 0.5f);
    }
}
