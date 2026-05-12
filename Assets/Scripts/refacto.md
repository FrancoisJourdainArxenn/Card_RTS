Ça fait sens. Le plan :

Nouveau script CurvedArrow — LineRenderer + courbe de Bezier + suivi souris (fusion de HoverArrow et BezierArrows)
DragCreatureActions passe de BezierArrows à CurvedArrow
EffectTargetingArrow utilise aussi CurvedArrow
Nouveau script CurvedArrow.cs :


using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class CurvedArrow : MonoBehaviour
{
    [SerializeField] private int resolution = 30;
    [SerializeField] private Vector2 cp1Factors = new Vector2(-0.3f, 0.8f);
    [SerializeField] private Vector2 cp2Factors = new Vector2(0.1f, 0.85f);

    private LineRenderer _line;
    private Transform _originOverride;
    private Transform Origin => _originOverride != null ? _originOverride : transform;

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
        enabled = false;
    }

    public void Show()
    {
        _originOverride = null;
        enabled = true;
    }

    public void ShowFromTransform(Transform origin)
    {
        _originOverride = origin;
        enabled = true;
    }

    public void Hide()
    {
        enabled = false;
        _line.positionCount = 0;
    }

    void Update()
    {
        DrawCurve(Origin.position, GetMouseWorldPosition());
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
    }

    private static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u*u*u * p0 + 3*u*u*t * p1 + 3*u*t*t * p2 + t*t*t * p3;
    }

    private Vector3 GetMouseWorldPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane gamePlane = new Plane(Vector3.up, Origin.position);
        return gamePlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : Origin.position;
    }
}
Les cp1Factors / cp2Factors reprennent exactement les valeurs hardcodées de BezierArrows, donc la courbe sera identique visuellement.

Dans DragCreatureActions, une seule ligne change :


// avant
[SerializeField] private BezierArrows targettingArrow;
// après
[SerializeField] private CurvedArrow targettingArrow;
Et dans EffectTargetingArrow :


[SerializeField] private CurvedArrow arrow;

private void OnTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
{
    GameObject sourceObject = IDHolder.GetGameObjectWithID(queue[currentIndex].SourceEntityID);
    if (sourceObject == null) return;
    arrow.ShowFromTransform(sourceObject.transform);
}

private void OnTargetingEnded() => arrow.Hide();
Dans Unity, tu devras re-assigner la référence targettingArrow sur le prefab de créature après le changement de type.