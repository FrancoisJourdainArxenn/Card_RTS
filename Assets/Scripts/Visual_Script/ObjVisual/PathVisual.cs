using System;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class PathVisual : MonoBehaviour
{
    [SerializeField] private int _resolution = 20;
    [SerializeField] private float _curveOffset = 1.5f;
    [SerializeField] private Color _openColor = Color.blue;
    [SerializeField] private Color _blockedColor = Color.red;
    private static event Action OnRefreshRequested;
    public static void RefreshAll() => OnRefreshRequested?.Invoke();

    private LineRenderer _lineRenderer;
    private ZonePath _path;

    void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
    }

    public void Initialize(ZonePath path)
    {
        _path = path;
        _path.Logic.OnBlockedChanged += UpdateColor;
        OnRefreshRequested += UpdateColor;

        DrawCurve();
        UpdateColor();
    }

    void OnDestroy()
    {
        if (_path?.Logic != null)
            _path.Logic.OnBlockedChanged -= UpdateColor;
        OnRefreshRequested -= UpdateColor;
    }

    private void DrawCurve()
    {
        Vector3 a = _path.ZoneA.transform.position;
        Vector3 b = _path.ZoneB.transform.position;
        Vector3 dir = (b - a).normalized;

        float diagonality = 2f * Mathf.Abs(dir.x) * Mathf.Abs(dir.z);

        Vector3 mid = (a + b) * 0.5f;
        Vector3 perpendicular = Vector3.Cross(dir, Vector3.up).normalized;
        Vector3 controlPoint = mid + perpendicular * (_curveOffset * diagonality);

        _lineRenderer.positionCount = _resolution + 1;
        Vector3[] positions = new Vector3[_resolution + 1];
        for (int i = 0; i <= _resolution; i++)
        {
            float t = i / (float)_resolution;
            float u = 1f - t;
            positions[i] = u * u * a + 2f * u * t * controlPoint + t * t * b;
        }
        _lineRenderer.SetPositions(positions);
    }

    private void UpdateColor()
    {
        bool isBlocked = _path.Logic.IsBlocked;

        if (!isBlocked && !_path.Logic.IsLateral)
        {
            Player local = GlobalSettings.Instance.localPlayer;
            bool isLowPlayer = local == GlobalSettings.Instance.LowPlayer;

            ZoneLogic fromLogic   = isLowPlayer ? _path.Logic.ZoneA  : _path.Logic.ZoneB;
            ZoneManager fromZone  = isLowPlayer ? _path.ZoneA        : _path.ZoneB;

            bool hasvision = !FogOfWarManager.Instance.IsZoneFogged(fromZone);
            isBlocked = hasvision && !_path.Logic.CanTraverse(local, fromLogic);
        }

        Color c = isBlocked ? _blockedColor : _openColor;
        _lineRenderer.startColor = c;
        _lineRenderer.endColor = c;
    }



}
