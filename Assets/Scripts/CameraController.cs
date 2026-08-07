using System.Collections;
using System.Collections.Generic;
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

    [Header("Right-click drag inertia")]
    public float panInertiaDamping = 4f;
    public float panInertiaMinSpeed = 0.05f;
    public float panVelocitySampleWindow = 0.15f;

    enum State { Overview, Transitioning, ZoomedIn, BattleCam }
    State _state = State.Overview;
    float _zoomT = 0f;
    Vector3 _panPosition;
    bool _isPanDragging;
    Vector3 _panDragWorldAnchor;
    Vector3 _panVelocity;
    readonly Queue<(Vector3 delta, float dt)> _panDragSamples = new();
    Vector3 _panDragSampleDisplacementSum;
    float _panDragSampleTimeSum;

    public bool IsZoomedIn => _state == State.ZoomedIn;
    public bool IsBattleCam => _state == State.BattleCam;
    public Vector3 WorldPosition => transform.position;
    public ZoneCameraAnchor CurrentAnchor { get; private set; }
    Vector3 _savedPosition;
    Quaternion _savedRotation;
    Quaternion _defaultRotation;

    ZoneCameraAnchor _hoveredAnchor;

    ZoneCameraAnchor _battleCamAnchor;
    Vector3 _preBattlePanPosition;

    [Header("Camera Shake")]
    public int shakeVibrato = 20;
    public float shakeRandomness = 90f;
    Tween _shakeTween;

    public static CameraController Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

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
        Vector3 pos = transform.position;
        _panPosition = pos;
        _zoomT = 0.5f;
        float startHeight = Mathf.Lerp(MapManager.Current.cameraHeight, MapManager.Current.topHeight, _zoomT);
        transform.position = new Vector3(pos.x, startHeight, pos.z);
        _defaultRotation = transform.rotation;
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
            case State.BattleCam:
                // Camera is locked on the active combat zone: no pan, no zoom, no manual control.
                break;
        }
    }

    Vector3 GetKeyboardEdgeDirection()
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

    // Right-click-drag pan: tracks the ground point under the cursor 1:1 (like a
    // touch-drag), independent of middlePanSpeed/zoomSmoothSpeed.
    bool TryGetMouseDragPan(out Vector3 panDelta)
    {
        panDelta = Vector3.zero;

        if (Input.GetMouseButtonDown(1))
        {
            _isPanDragging = true;
            _panDragWorldAnchor = RaycastGround(Input.mousePosition);
            _panVelocity = Vector3.zero;
            _panDragSamples.Clear();
            _panDragSampleDisplacementSum = Vector3.zero;
            _panDragSampleTimeSum = 0f;
            return false;
        }

        if (!Input.GetMouseButton(1))
        {
            _isPanDragging = false;
            return false;
        }

        if (!_isPanDragging)
            return false;

        Vector3 worldNow = RaycastGround(Input.mousePosition);
        panDelta = _panDragWorldAnchor - worldNow;
        return true;
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

        Vector3 direction = GetKeyboardEdgeDirection();
        if (direction == Vector3.zero)
            return;

        MoveCameraToClosestBase(transform.position, direction);
    }

    void HandleOverviewPan()
    {
        // Pan always updates the underlying Middle-level position; its visual
        // effect fades out as _zoomT approaches 1 (Top), so no pan is felt there.
        bool isDragPanning = TryGetMouseDragPan(out Vector3 dragDelta);
        if (isDragPanning)
        {
            _panPosition += new Vector3(dragDelta.x, 0f, dragDelta.z);
            ClampPanPosition();

            // Average the displacement over a short sliding time window rather than the
            // instantaneous per-frame delta, so a single jittery frame right before release
            // can't dominate the inertia — it reflects the cursor's recent average speed.
            _panDragSamples.Enqueue((dragDelta, Time.deltaTime));
            _panDragSampleDisplacementSum += dragDelta;
            _panDragSampleTimeSum += Time.deltaTime;
            while (_panDragSampleTimeSum > panVelocitySampleWindow && _panDragSamples.Count > 1)
            {
                (Vector3 delta, float dt) oldest = _panDragSamples.Dequeue();
                _panDragSampleDisplacementSum -= oldest.delta;
                _panDragSampleTimeSum -= oldest.dt;
            }
            _panVelocity = _panDragSampleTimeSum > 0.0001f
                ? _panDragSampleDisplacementSum / _panDragSampleTimeSum
                : Vector3.zero;
        }
        else if (!_isPanDragging)
        {
            Vector3 direction = GetKeyboardEdgeDirection();
            if (direction != Vector3.zero)
            {
                _panVelocity = Vector3.zero;
                _panPosition += direction.normalized * middlePanSpeed * Time.deltaTime;
                ClampPanPosition();
            }
            else if (_panVelocity.sqrMagnitude > panInertiaMinSpeed * panInertiaMinSpeed)
            {
                Vector3 preClamp = _panPosition + _panVelocity * Time.deltaTime;
                _panPosition = preClamp;
                ClampPanPosition();
                // Kill inertia on whichever axis just got stopped by the map bounds.
                if (!Mathf.Approximately(_panPosition.x, preClamp.x)) _panVelocity.x = 0f;
                if (!Mathf.Approximately(_panPosition.z, preClamp.z)) _panVelocity.z = 0f;

                _panVelocity *= Mathf.Exp(-panInertiaDamping * Time.deltaTime);
            }
            else
            {
                _panVelocity = Vector3.zero;
            }
        }

        // TEST — désactivé : le zoom manuel sur une zone (survol + molette) est coupé,
        // la vue ZoomedIn n'est désormais accessible que via la BattleCam. Code conservé
        // pour pouvoir revenir en arrière facilement.
        // var closest = FindClosestAnchorToScreenPoint(Input.mousePosition, out float screenDist);
        // bool withinRadius = closest != null && screenDist <= topViewZoomInRadiusPixels;
        // SetHoveredAnchor(withinRadius ? closest : null);

        float scroll = Input.mouseScrollDelta.y;
        // if (scroll > 0f && withinRadius)
        // {
        //     SaveMiddleReturnPoint(_panPosition);
        //     SetHoveredAnchor(null);
        //     MoveCameraToAnchor(closest);
        //     return;
        // }
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

        if (isDragPanning)
        {
            // Bypass the easing above for XZ: the drag must track the cursor 1:1, with
            // no lag, while the height (y) keeps easing normally for zoom transitions.
            Vector3 pos = transform.position;
            transform.position = new Vector3(targetPos.x, pos.y, targetPos.z);
        }
    }

    void ClampPanPosition()
    {
        MapManager map = MapManager.Current;
        if (!map.clampMiddlePan)
            return;
        Vector2 min = Vector2.Lerp(map.middlePanMin, map.topPanMin, _zoomT);
        Vector2 max = Vector2.Lerp(map.middlePanMax, map.topPanMax, _zoomT);
        _panPosition.x = Mathf.Clamp(_panPosition.x, min.x, max.x);
        _panPosition.z = Mathf.Clamp(_panPosition.z, min.y, max.y);
    }

    Vector3 RaycastGround(Vector2 screenPos)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);
        return transform.position;
    }

    ZoneCameraAnchor FindClosestAnchorToScreenPoint(Vector2 screenPos, out float screenDist)
    {
        ZoneCameraAnchor best = null;
        float bestDist = float.MaxValue;
        foreach (ZoneCameraAnchor anchor in ZoneCameraAnchor.All)
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
        // complete=true : si un tween est déjà en vol (seul cas possible : ExitBattleCam ou
        // FocusBattleCamOn qui interrompt un FocusBattleCamOn précédent — les contrôles manuels
        // ne peuvent pas re-déclencher TransitionTo pendant State.Transitioning), on force son
        // OnComplete à s'exécuter avant d'enchaîner. Sinon DOKill() abandonne silencieusement le
        // callback en attente (ex. CreatureAttackCommand.PlayAttack), ce qui gèle la file de
        // commandes pour toujours (Command.playingQueue reste true).
        transform.DOKill(true);
        _state = State.Transitioning;
        transform.DOMove(targetPos, transitionDuration).SetEase(transitionEase);
        transform.DORotateQuaternion(targetRot, transitionDuration).SetEase(transitionEase)
                 .OnComplete(() => onComplete());
    }

    // -------------------------------------------------------------------------
    // BATTLE CAM — automatic camera focus during the Battle phase.
    // While active, Update() dispatches to no pan/zoom handler, so manual
    // controls are locked out for the whole duration (see State.BattleCam).
    // -------------------------------------------------------------------------

    public void EnterBattleCam()
    {
        if (_state == State.BattleCam)
            return;
        SetHoveredAnchor(null);
        _preBattlePanPosition = _panPosition;
        _battleCamAnchor = null;
        _state = State.BattleCam;
    }

    // Called whenever a combat step is about to start executing; moves the camera to the
    // zone closest to worldPos if it's not already there, and only invokes onArrived once
    // the camera has actually settled on that zone — callers should hold off playing the
    // attack visual until then, so combat never plays out ahead of the camera.
    public void FocusBattleCamOn(Vector3 worldPos, System.Action onArrived)
    {
        if (_state != State.BattleCam && _state != State.Transitioning)
        {
            onArrived?.Invoke();
            return;
        }
        ZoneCameraAnchor anchor = ZoneCameraAnchor.FindClosestTo(worldPos);
        if (anchor == null || (anchor == _battleCamAnchor && _state == State.BattleCam))
        {
            onArrived?.Invoke();
            return;
        }
        _battleCamAnchor = anchor;
        TransitionTo(anchor.transform.position, anchor.transform.rotation, () => { _state = State.BattleCam; onArrived?.Invoke(); });
    }

    public void ExitBattleCam()
    {
        if (_state != State.BattleCam && _state != State.Transitioning)
            return;
        _battleCamAnchor = null;
        _panPosition = _preBattlePanPosition;
        Vector3 middlePos = new Vector3(_preBattlePanPosition.x, MapManager.Current.cameraHeight, _preBattlePanPosition.z);
        TransitionTo(middlePos, _defaultRotation, () => { _state = State.Overview; _zoomT = 0f; CurrentAnchor = null; });
    }

    void MoveCameraToClosestBase(Vector3 pos, Vector3? direction = null)
    {
        ZoneCameraAnchor nearest = direction.HasValue
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

    // -------------------------------------------------------------------------
    // CAMERA SHAKE — triggered when a unit's Attack stat crosses the light/strong
    // thresholds configured on GlobalSettings while it deals damage.
    // -------------------------------------------------------------------------

    // uniqueID is the ID of the unit whose Attack stat determines shake strength
    // (the attacker for damage it deals, or the defender for counter-damage it deals back).
    public void ShakeForAttackerID(int uniqueID)
    {
        int attack = GetAttackValue(uniqueID);
        if (attack < 0)
            return;
        ShakeForAttackValue(attack);
    }

    public void ShakeForAttackValue(int attackValue)
    {
        GlobalSettings gs = GlobalSettings.Instance;
        if (gs == null)
            return;

        if (attackValue >= gs.CameraShakeThresholdStrong)
            Shake(gs.CameraShakeDurationStrong, gs.CameraShakeStrengthStrong);
        else if (attackValue >= gs.CameraShakeThresholdLight)
            Shake(gs.CameraShakeDurationLight, gs.CameraShakeStrengthLight);
    }

    void Shake(float duration, float strength)
    {
        _shakeTween?.Kill();
        _shakeTween = transform.DOShakePosition(duration, strength, shakeVibrato, shakeRandomness, false, true)
            .SetLink(gameObject);
    }

    static int GetAttackValue(int uniqueID)
    {
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(uniqueID, out CreatureLogic c)) return c.Attack;
        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(uniqueID, out BuildingLogic b)) return b.Attack;
        return -1;
    }
}
