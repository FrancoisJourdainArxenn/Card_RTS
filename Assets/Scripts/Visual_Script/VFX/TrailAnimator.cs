using UnityEngine;
using System.Collections;

public class TrailAnimator : MonoBehaviour
{
    [Header("Animation")]
    [SerializeField] private float duration = 0.6f;
    [SerializeField] private float arcHeight = 3f;

    [Header("Zoom")]
    [SerializeField] private float zoomedArcMultiplier = 0.4f;

    private TrailRenderer[] _trails;

    void Awake()
    {
        _trails = GetComponentsInChildren<TrailRenderer>(true);
    }

    public void Play(Transform startTransform, Transform endTransform, bool isZoomed)
    {
        float effectiveArc = isZoomed ? arcHeight * zoomedArcMultiplier : arcHeight;
        StartCoroutine(Animate(startTransform.position, endTransform.position, effectiveArc));
    }

    private IEnumerator Animate(Vector3 start, Vector3 end, float arcHeight)
    {
        transform.position = start;

        foreach (var t in _trails)
        {
            t.Clear();
            t.emitting = true;
        }

        Vector3 apex = (start + end) * 0.5f + Vector3.up * arcHeight;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.position = Bezier(start, apex, end, t);
            yield return null;
        }

        transform.position = end;

        foreach (var t in _trails)
            t.emitting = false;

        float maxTrailTime = 0f;
        foreach (var t in _trails)
            maxTrailTime = Mathf.Max(maxTrailTime, t.time);

        yield return new WaitForSeconds(maxTrailTime);
        Destroy(gameObject);
    }

    private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }
}
