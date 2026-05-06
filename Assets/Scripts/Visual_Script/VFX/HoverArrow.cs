using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class HoverArrow : MonoBehaviour
{
    [SerializeField] private float arcHeight = 3f;
    [SerializeField] private int resolution = 30;

    private LineRenderer _line;
    private Vector3 _start;
    private Vector3 _end;
    private bool _active;

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.enabled = false;
    }

    void Update()
    {
        if (_active)
            DrawCurve();
    }

    public void Show(Vector3 start, Vector3 end)
    {
        _start = start;
        _end = end;
        _active = true;
        _line.enabled = true;
        DrawCurve();
    }

    public void UpdateEnd(Vector3 end)
    {
        _end = end;
    }

    public void Hide()
    {
        _active = false;
        _line.enabled = false;
    }

    private void DrawCurve()
    {
        _line.positionCount = resolution;
        Vector3 apex = (_start + _end) * 0.5f + Vector3.up * arcHeight;

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
