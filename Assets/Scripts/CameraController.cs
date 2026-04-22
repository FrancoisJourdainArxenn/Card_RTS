using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

public class CameraController : MonoBehaviour
{
    public float defaultPanSpeed = 20f;
    public float zoomedInDefaultPanSpeed = 10f;
    public float panBorderThicknessVertical = 50f;
    public float panBorderThicknessHorizontal = 250f;
    public Vector2 panLimit;
    public Vector2 zoomedInPanLimit;
    private Vector2 panCurrentSpeed = Vector2.zero;
    private Vector2 panAcceleration = Vector2.zero;
    public float maxSpeed = 5f;
    public float maxAcceleration = 75f;
    public float AccelerationUnit = 0.03f;
    public float decelerationFactor = 0.9f;

    [Header("Zone lock")]
    [Range(0f, 2f)]
    public float transitionDuration = 1.25f;
    public Ease transitionEase = Ease.OutSine;
    [Range(0f, 2f)]
    public float snapBackDelay = 0.3f;

    enum State { Overhead, Transitioning, ZoomedIn }
    State _state = State.Overhead;
    Vector3 _savedOverheadPosition;
    Quaternion _savedOverheadRotation;

    ZoneCameraAnchor _hoveredAnchor;
    bool _zoomedPanOnGoing;
    Coroutine _snapBackCoroutine;



    void Start() => StartCoroutine(WaitForLocalPlayer());

    IEnumerator WaitForLocalPlayer()
    {
        yield return new WaitUntil(() =>
            GlobalSettings.Instance.localPlayer != null &&
            GlobalSettings.Instance.localPlayer.MainPArea != null);

        Vector3 basePos = GlobalSettings.Instance.localPlayer.MainPArea.transform.position;
        transform.position = new Vector3(basePos.x, 50f, basePos.z);
    }

    void Update()
    {
        if (_state == State.Transitioning) return;

        UpdatePanAcceleration();
        if (_state == State.ZoomedIn)
        {
            if (Input.GetAxis("Mouse ScrollWheel") < 0f)
                TransitionTo(_savedOverheadPosition, _savedOverheadRotation,
                             () => _state = State.Overhead);            
            if (panAcceleration.magnitude > 0)
            {
                if (_snapBackCoroutine != null)
                {
                    StopCoroutine(_snapBackCoroutine);
                    _snapBackCoroutine = null;
                }
                _zoomedPanOnGoing = true;
                HandlePan(zoomedInDefaultPanSpeed, zoomedInPanLimit);
            }
            else if (_zoomedPanOnGoing)
            {
                _zoomedPanOnGoing = false;
                _snapBackCoroutine = StartCoroutine(SnapBackAfterDelay());
                HandlePan(zoomedInDefaultPanSpeed, zoomedInPanLimit);
            }
            else if (_snapBackCoroutine != null)
            {
                // drift phase: continue panning until snap triggers
                HandlePan(zoomedInDefaultPanSpeed, zoomedInPanLimit);
            }
            return;
        }

        // Overhead state
        HandlePan(defaultPanSpeed, panLimit);
        var hovered = ZoneCameraAnchor.FindClosestTo(GetMouseWorldPosition());
        if (hovered != _hoveredAnchor)
        {
            _hoveredAnchor?.SetHighlighted(false);
            _hoveredAnchor = hovered;
            _hoveredAnchor?.SetHighlighted(true);
        }
        if (Input.GetAxis("Mouse ScrollWheel") > 0f)
        {
            var anchor = ZoneCameraAnchor.FindClosestTo(GetMouseWorldPosition());
            if (anchor != null)
            {
                _savedOverheadPosition = transform.position;
                _savedOverheadRotation = transform.rotation;
                _hoveredAnchor?.SetHighlighted(false);
                _hoveredAnchor = null;
                _zoomedPanOnGoing = false;
                TransitionTo(anchor.transform.position, anchor.transform.rotation,
                             () => _state = State.ZoomedIn);
            }
        }
    }

    void UpdatePanAcceleration()
    {
        panAcceleration = Vector2.zero;

        if (Input.mousePosition.y >= Screen.height - panBorderThicknessVertical / 1 + panCurrentSpeed.y)
            panAcceleration.y += Input.mousePosition.y - (Screen.height - panBorderThicknessVertical);

        if (Input.mousePosition.y <= panBorderThicknessVertical / 1 + panCurrentSpeed.y)
            panAcceleration.y -= panBorderThicknessVertical - Input.mousePosition.y;

        if (Input.mousePosition.x >= Screen.width - panBorderThicknessHorizontal / 1 + panCurrentSpeed.x)
            panAcceleration.x += Input.mousePosition.x - (Screen.width - panBorderThicknessHorizontal);

        if (Input.mousePosition.x <= panBorderThicknessHorizontal / 1 + panCurrentSpeed.x)
            panAcceleration.x -= panBorderThicknessHorizontal - Input.mousePosition.x;

        if (Input.GetKey("w"))
            panAcceleration.y += panBorderThicknessVertical;

        if (Input.GetKey("s"))
            panAcceleration.y -= panBorderThicknessVertical;
        
        if (Input.GetKey("d"))
            panAcceleration.x += panBorderThicknessHorizontal;

        if (Input.GetKey("a"))
            panAcceleration.x -= panBorderThicknessHorizontal;

        
        // Debug.Log("Local Pan Acceleration: " + localpanAcceleration);

        panAcceleration.x = Mathf.Clamp(panAcceleration.x, -maxAcceleration, maxAcceleration);
        panAcceleration.y = Mathf.Clamp(panAcceleration.y, -maxAcceleration, maxAcceleration);

    }
    void HandlePan(float speed, Vector2? clampLimit = null)
    {
        Vector3 pos = transform.position;
        float delta = speed * Time.deltaTime;

        if (panAcceleration.y != 0)
            panCurrentSpeed.y += panAcceleration.y * Time.deltaTime * AccelerationUnit;
        else
        {
            float newSpeed = panCurrentSpeed.y > 0 ?
                Mathf.Max(panCurrentSpeed.y * decelerationFactor, panCurrentSpeed.y - maxAcceleration):
                Mathf.Min(panCurrentSpeed.y * decelerationFactor, panCurrentSpeed.y + maxAcceleration); 
            panCurrentSpeed.y = newSpeed;
            if (Mathf.Abs(panCurrentSpeed.y) < 0.001f)
                panCurrentSpeed.y = 0f;
        }

        if (panAcceleration.x != 0)
            panCurrentSpeed.x += panAcceleration.x * Time.deltaTime * AccelerationUnit;
        else
        {
            float newSpeed = panCurrentSpeed.x > 0 ?
                Mathf.Max(panCurrentSpeed.x * decelerationFactor, panCurrentSpeed.x - maxAcceleration):
                Mathf.Min(panCurrentSpeed.x * decelerationFactor, panCurrentSpeed.x + maxAcceleration);
            panCurrentSpeed.x = newSpeed;
            if (Mathf.Abs(panCurrentSpeed.x) < 0.001f)
                panCurrentSpeed.x = 0f;
        }
        
        // Debug.Log("Pan Acceleration: " + panAcceleration);

        panCurrentSpeed.x = Mathf.Clamp(panCurrentSpeed.x, -maxSpeed, maxSpeed);
        panCurrentSpeed.y = Mathf.Clamp(panCurrentSpeed.y, -maxSpeed, maxSpeed);

        // Debug.Log("Clamped Pan Acceleration: " + panAcceleration);

        pos.z += panCurrentSpeed.y * delta;
        pos.x += panCurrentSpeed.x * delta;

        if (clampLimit.HasValue)
        {
            pos.x = Mathf.Clamp(pos.x, -clampLimit.Value.x, clampLimit.Value.x);
            pos.z = Mathf.Clamp(pos.z, -clampLimit.Value.y, clampLimit.Value.y);
        }

        transform.position = pos;
    }

    void TransitionTo(Vector3 targetPos, Quaternion targetRot, System.Action onComplete)
    {
        _state = State.Transitioning;
        transform.DOMove(targetPos, transitionDuration).SetEase(transitionEase);
        transform.DORotateQuaternion(targetRot, transitionDuration).SetEase(transitionEase)
                 .OnComplete(() => onComplete());
    }

    Vector3 GetMouseWorldPosition()
    {
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);
        return transform.position;
    }
    IEnumerator SnapBackAfterDelay()
    {
        yield return new WaitForSeconds(snapBackDelay);
        _snapBackCoroutine = null;
        if (_state != State.ZoomedIn) yield break;
        if (Draggable.DraggingThis != null) yield break;  // drag in progress, abort
        var nearest = ZoneCameraAnchor.FindClosestTo(transform.position);
        if (nearest != null)
        {
            TransitionTo(nearest.transform.position, nearest.transform.rotation,
                        () => _state = State.ZoomedIn);
            panCurrentSpeed = Vector2.zero;
        }
    }

    bool HasZoomedPanInput()
    {
        return panAcceleration.x != 0 || panAcceleration.y != 0;
    }

}
