using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class CurvedArrow : MonoBehaviour
{
    [SerializeField] private int resolution = 30;
    [SerializeField] private Vector2 cp1Factors = new Vector2(-0.3f, 0.8f);
    [SerializeField] private Vector2 cp2Factors = new Vector2(0.1f, 0.85f);
    [SerializeField] private Transform arrowHead;

    private LineRenderer _line;
    private Vector3 _fixedStart;

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.positionCount = 0;
        enabled = false;
    }

    public void Show()
    {
        _fixedStart = transform.position;
        if (arrowHead != null) arrowHead.gameObject.SetActive(true);
        enabled = true;
    }

    public void Hide()
    {
        enabled = false;
        _line.positionCount = 0;
        if (arrowHead != null) arrowHead.gameObject.SetActive(false);
    }

    void Update()
    {
        DrawCurve(_fixedStart, GetMouseWorldPosition());
    }

    private void DrawCurve(Vector3 start, Vector3 end)
    {
        _line.positionCount = resolution;
        Vector3 delta = end - start;
        Vector3 p1 = start + new Vector3(delta.x * cp1Factors.x, 0f, delta.z * cp1Factors.y);
        Vector3 p2 = start + new Vector3(delta.x * cp2Factors.x, 0f, delta.z * cp2Factors.y);

        for (int i = 0; i < resolution; i++)
        {
            float t = i / (float)(resolution - 1);
            _line.SetPosition(i, CubicBezier(start, p1, p2, end, t));
        }

        UpdateArrowHead(end, p2);
    }

    private void UpdateArrowHead(Vector3 tip, Vector3 beforeTip)
    {
        if (arrowHead == null) return;

        arrowHead.position = tip;

        // Tangente de la courbe en t=1 : direction de la dernière portion
        Vector3 tangent = new Vector3(tip.x - beforeTip.x, 0f, tip.z - beforeTip.z);
        if (tangent.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
            arrowHead.rotation = Quaternion.Euler(90f, angle, 0f);
        }
    }

    private static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u*u*u * p0 + 3*u*u*t * p1 + 3*u*t*t * p2 + t*t*t * p3;
    }

    private Vector3 GetMouseWorldPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane gamePlane = new Plane(Vector3.up, new Vector3(0f, _fixedStart.y, 0f));
        return gamePlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : _fixedStart;
    }
}
