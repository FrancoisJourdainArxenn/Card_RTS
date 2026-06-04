using DG.Tweening;
using UnityEngine;

public static class ValuePopAnimation
{
    public static void Pop(Transform t)
    {
        float strength = GlobalSettings.Instance != null ? GlobalSettings.Instance.popStrength : 0.35f;
        float duration = GlobalSettings.Instance != null ? GlobalSettings.Instance.popDuration : 0.35f;
        t.DOKill();
        t.localScale = Vector3.one;
        t.DOPunchScale(Vector3.one * strength, duration, 1, 0.5f);
    }
}
