using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class HoverArrow : MonoBehaviour
{
    [SerializeField] private float arcHeight = 3f;
    [SerializeField] private int resolution = 30;
    private Transform _startTransform;
    private Transform _endTransform;
    [Header("ArrowHead")]
    [SerializeField] private Transform _arrowHead;
    [SerializeField] private float _arrowHeadPitch = 35f;

    private Vector3 _apex;
    private LineRenderer _line;
    private Vector3 _start;
    private Vector3 _end;
    private bool _followMouse;


    public void Show(Transform start, Transform end)
    {
        if(_line == null) _line = GetComponent<LineRenderer>();
        _startTransform = start;
        _endTransform = end;
        _followMouse = false;
        gameObject.SetActive(true);
    }

    public void ShowToMouse()
    {
        if (_line == null) _line = GetComponent<LineRenderer>();
        _startTransform = transform;
        _followMouse = true;
        gameObject.SetActive(true);
    }


    void Update()
    {
        if (_startTransform == null) return;

        _start = _startTransform.position;
        _end = _followMouse ? GetMouseWorldPosition() : _endTransform.position;
        DrawCurve();

        if (_arrowHead != null)
        {
            _arrowHead.position = _end;
            Vector3 tangent = (_end - _apex).normalized;
            if (tangent != Vector3.zero)
            {
                Quaternion look = Quaternion.LookRotation(tangent, Vector3.up);
                _arrowHead.rotation = look * Quaternion.Euler(_arrowHeadPitch, 0f, 0f);
            }
        }
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
        _apex = mid + perp * arcHeight;

        for (int i = 0; i < resolution; i++)
        {
            float t = i / (float)(resolution - 1);
            _line.SetPosition(i, Bezier(_start, _apex, _end, t));
        }
    }

    private Vector3 GetMouseWorldPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane gamePlane = new Plane(Vector3.up, _startTransform.position);
        return gamePlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : _startTransform.position;
    }
    private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }
}
