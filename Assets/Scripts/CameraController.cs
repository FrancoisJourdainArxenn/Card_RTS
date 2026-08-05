using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

public class CameraController : MonoBehaviour
{
    public float panBorderThicknessVertical;
    public float panBorderThicknessHorizontal;

    [Header("Middle view (default overview)")]
    [FormerlySerializedAs("overheadPanSpeed")]
    public float middlePanSpeed = 20f;

    [Header("Top view (max zoom out)")]
    public float topViewZoomInRadiusPixels = 80f;

    [Header("Overview zoom (continuous Middle <-> Top)")]
    public float zoomStep = 0.08f;
    public float zoomSmoothSpeed = 8f;

    [Header("Zone lock")]
    [Range(0f, 2f)]
    public float transitionDuration = 0.7f;
    public Ease transitionEase = Ease.InOutCubic;

    enum State { Overview, Transitioning, ZoomedIn }
    State _state = State.Overview;
    float _zoomT = 0f;
    Vector3 _panPosition;

    public bool IsZoomedIn => _state == State.ZoomedIn;
    public Vector3 WorldPosition => transform.position;
    public ZoneCameraAnchor CurrentAnchor { get; private set; }
    Vector3 _savedPosition;
    Quaternion _savedRotation;

    ZoneCameraAnchor _hoveredAnchor;

    // void Start() => StartCoroutine(WaitForLocalPlayer());

    // IEnumerator WaitForLocalPlayer()
    // {
    //     yield return new WaitUntil(() =>
    //         GlobalSettings.Instance.localPlayer != null &&
    //         GlobalSettings.Instance.localPlayer.MainPArea != null);

    //     Vector3 basePos = GlobalSettings.Instance.localPlayer.MainPArea.transform.position;
    //     transform.position = new Vector3(basePos.x, mapManager.cameraHeight, basePos.z);
    // }

    void Start()
    {
        var pos = transform.position;
        _panPosition = pos;
        _zoomT = 0.5f;
        float startHeight = Mathf.Lerp(MapManager.Current.cameraHeight, MapManager.Current.topHeight, _zoomT);
        transform.position = new Vector3(pos.x, startHeight, pos.z);
    }


    void Update()
    {
        switch (_state)
        {
            case State.Transitioning:
                break;
            case State.ZoomedIn:
                HandleZoomedInPan();
                break;
            case State.Overview:
                HandleOverviewPan();
                break;
        }
    }

    Vector3 GetPanDirection()
    {
        Vector3 direction = Vector3.zero;
        if (Input.mousePosition.y >= Screen.height - panBorderThicknessVertical || Input.GetKey("w"))
            direction.z += 1;
        if (Input.mousePosition.y <= panBorderThicknessVertical || Input.GetKey("s"))
            direction.z -= 1;
        if (Input.mousePosition.x >= Screen.width - panBorderThicknessHorizontal || Input.GetKey("d"))
            direction.x += 1;
        if (Input.mousePosition.x <= panBorderThicknessHorizontal || Input.GetKey("a"))
            direction.x -= 1;
        return direction;
    }

    void HandleZoomedInPan()
    {
        if (Input.mouseScrollDelta.y < 0f)
        {
            TransitionTo(
                _savedPosition,
                _savedRotation,
                () => { _state = State.Overview; _zoomT = 0f; CurrentAnchor = null; }
            );
            return;
        }

        Vector3 direction = GetPanDirection();
        if (direction == Vector3.zero)
            return;

        MoveCameraToClosestBase(transform.position, direction);
    }

    void HandleOverviewPan()
    {
        // Pan always updates the underlying Middle-level position; its visual
        // effect fades out as _zoomT approaches 1 (Top), so no pan is felt there.
        Vector3 direction = GetPanDirection();
        if (direction != Vector3.zero)
        {
            Vector3 newPan = _panPosition + direction.normalized * middlePanSpeed * Time.deltaTime;
            _panPosition = newPan;
            ClampPanPosition();
        }

        var closest = FindClosestAnchorToScreenPoint(Input.mousePosition, out float screenDist);
        bool withinRadius = closest != null && screenDist <= topViewZoomInRadiusPixels;
        SetHoveredAnchor(withinRadius ? closest : null);

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f && withinRadius)
        {
            SaveMiddleReturnPoint(_panPosition);
            SetHoveredAnchor(null);
            MoveCameraToAnchor(closest);
            return;
        }
        if (scroll != 0f)
        {
            Vector3 worldBefore = RaycastGround(Input.mousePosition);

            _zoomT = Mathf.Clamp01(_zoomT - scroll * zoomStep);

            // Trial-move to the new height so we can measure where the cursor
            // now points, then shift the pan focus to compensate: this keeps
            // the world point under the cursor fixed on screen (zoom-to-cursor).
            float newHeight = Mathf.Lerp(MapManager.Current.cameraHeight, MapManager.Current.topHeight, _zoomT);
            Vector3 originalPos = transform.position;
            transform.position = new Vector3(_panPosition.x, newHeight, _panPosition.z);
            Vector3 worldAfter = RaycastGround(Input.mousePosition);
            transform.position = originalPos;

            Vector3 correction = worldBefore - worldAfter;
            _panPosition += new Vector3(correction.x, 0f, correction.z);
            ClampPanPosition();
        }

        Vector3 targetPos = new Vector3(_panPosition.x, Mathf.Lerp(MapManager.Current.cameraHeight, MapManager.Current.topHeight, _zoomT), _panPosition.z);
        transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-zoomSmoothSpeed * Time.deltaTime));
    }

    void ClampPanPosition()
    {
        var map = MapManager.Current;
        if (!map.clampMiddlePan)
            return;
        Vector2 min = Vector2.Lerp(map.middlePanMin, map.topPanMin, _zoomT);
        Vector2 max = Vector2.Lerp(map.middlePanMax, map.topPanMax, _zoomT);
        _panPosition.x = Mathf.Clamp(_panPosition.x, min.x, max.x);
        _panPosition.z = Mathf.Clamp(_panPosition.z, min.y, max.y);
    }

    Vector3 RaycastGround(Vector2 screenPos)
    {
        var ray = Camera.main.ScreenPointToRay(screenPos);
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);
        return transform.position;
    }

    ZoneCameraAnchor FindClosestAnchorToScreenPoint(Vector2 screenPos, out float screenDist)
    {
        ZoneCameraAnchor best = null;
        float bestDist = float.MaxValue;
        foreach (var anchor in ZoneCameraAnchor.All)
        {
            Vector3 groundPos = new Vector3(anchor.transform.position.x, 0f, anchor.transform.position.z);
            float d = Vector2.Distance(Camera.main.WorldToScreenPoint(groundPos), screenPos);
            if (d < bestDist)
            {
                bestDist = d;
                best = anchor;
            }
        }
        screenDist = bestDist;
        return best;
    }

    void SaveMiddleReturnPoint(Vector3 middleXZSource)
    {
        _savedPosition = new Vector3(middleXZSource.x, MapManager.Current.cameraHeight, middleXZSource.z);
        _savedRotation = transform.rotation;
    }

    void SetHoveredAnchor(ZoneCameraAnchor anchor)
    {
        if (anchor == _hoveredAnchor)
            return;
        _hoveredAnchor?.SetHighlighted(false);
        _hoveredAnchor = anchor;
        _hoveredAnchor?.SetHighlighted(true);
    }

    void TransitionTo(Vector3 targetPos, Quaternion targetRot, System.Action onComplete)
    {
        _state = State.Transitioning;
        transform.DOMove(targetPos, transitionDuration).SetEase(transitionEase);
        transform.DORotateQuaternion(targetRot, transitionDuration).SetEase(transitionEase)
                 .OnComplete(() => onComplete());
    }

    void MoveCameraToClosestBase(Vector3 pos, Vector3? direction = null)
    {
        var nearest = direction.HasValue
            ? ZoneCameraAnchor.FindClosestFollowingDirection(pos, direction.Value)
            : ZoneCameraAnchor.FindClosestTo(pos);
        if (nearest == null)
        {
            // Debug.Log("No anchor nearby");
            return;
        }
        MoveCameraToAnchor(nearest);
    }

    public void MoveCameraToAnchor(ZoneCameraAnchor anchor)
    {
        TransitionTo(
            anchor.transform.position,
            anchor.transform.rotation,
            () => { _state = State.ZoomedIn; CurrentAnchor = anchor; }
        );
    }
}
