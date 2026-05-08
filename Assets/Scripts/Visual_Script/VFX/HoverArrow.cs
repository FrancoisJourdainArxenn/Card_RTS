using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class HoverArrow : MonoBehaviour
{
    [SerializeField] private float arcHeight = 3f;
    [SerializeField] private int resolution = 30;
    private Transform _startTransform;
    private Transform _endTransform;

    private LineRenderer _line;
    private Vector3 _start;
    private Vector3 _end;
    private bool _active;

    public void Show(Transform start, Transform end)
    {
        if(_line == null) _line = GetComponent<LineRenderer>();
        _startTransform = start;
        _endTransform = end;
        gameObject.SetActive(true);
    }

    void Update()
    {
        _start = _startTransform.position;
        _end = _endTransform.position;
        DrawCurve();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void DrawCurve()
    {
        _line.positionCount = resolution;

        Vector3 mid = (_start + _end) * 0.5f;
        Vector3 dir = (_end - _start).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;
        Vector3 apex = mid + perp * arcHeight;

        for (int i = 0; i < resolution; i++)
        {
            float t = i / (float)(resolution - 1);
            _line.SetPosition(i, Bezier(_start, apex, _end, t));
        }
    }
    private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }
}
