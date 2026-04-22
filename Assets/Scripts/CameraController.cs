using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

public class CameraController : MonoBehaviour
{
    public float panSpeed = 20f;
    public float panBorderThickness = 200f;
    public Vector2 panLimit;
    private Vector2 panAcceleration = Vector2.zero;
    public float maxAcceleration = 2.5f;
    public float AccelerationUnit = 0.05f;
    public float decelerationFactor = 0.7f;

    [Header("Zone lock")]
    public float transitionDuration = 0.5f;
    [Range(0f, 50f)]
    public float zoomedInPanSpeed = 5f;
    public Ease transitionEase = Ease.InOutQuad;
    [Range(0f, 5f)]
    public float snapBackDelay = 1f;

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

        if (_state == State.ZoomedIn)
        {
            if (Input.GetAxis("Mouse ScrollWheel") < 0f)
                TransitionTo(_savedOverheadPosition, _savedOverheadRotation,
                             () => _state = State.Overhead);            
            if (HasZoomedPanInput())
            {
                if (_snapBackCoroutine != null)
                {
                    StopCoroutine(_snapBackCoroutine);
                    _snapBackCoroutine = null;
                }
                _zoomedPanOnGoing = true;
                HandlePan(zoomedInPanSpeed);
            }
            else if (_zoomedPanOnGoing)
            {
                _zoomedPanOnGoing = false;
                panAcceleration = Vector2.zero;
                _snapBackCoroutine = StartCoroutine(SnapBackAfterDelay());
            }
            return;
        }

        // Overhead state
        HandlePan(panSpeed, panLimit);
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

    void HandlePan(float speed, Vector2? clampLimit = null)
    {
        Vector3 pos = transform.position;
        float delta = speed * Time.deltaTime;
        Vector2 localpanAcceleration = Vector2.zero;

        if (Input.mousePosition.y >= Screen.height - panBorderThickness)
            localpanAcceleration.y += Input.mousePosition.y - (Screen.height - panBorderThickness);

        if (Input.mousePosition.y <= panBorderThickness)
            localpanAcceleration.y -= panBorderThickness - Input.mousePosition.y;

        if (Input.mousePosition.x >= Screen.width - panBorderThickness)
            localpanAcceleration.x += Input.mousePosition.x - (Screen.width - panBorderThickness);

        if (Input.mousePosition.x <= panBorderThickness)
            localpanAcceleration.x -= panBorderThickness - Input.mousePosition.x;

        if (Input.GetKey("w"))
            localpanAcceleration.y += panBorderThickness;

        if (Input.GetKey("s"))
            localpanAcceleration.y -= panBorderThickness;
        
        if (Input.GetKey("d"))
            localpanAcceleration.x += panBorderThickness;

        if (Input.GetKey("a"))
            localpanAcceleration.x -= panBorderThickness;

        
        // Debug.Log("Local Pan Acceleration: " + localpanAcceleration);

        if (localpanAcceleration.y != 0)
            panAcceleration.y += localpanAcceleration.y * Time.deltaTime * AccelerationUnit;
        else
        {
            panAcceleration.y *= decelerationFactor;
            if (panAcceleration.y < 0.01f && panAcceleration.y > -0.01f)
                panAcceleration.y = 0f;
        }

        if (localpanAcceleration.x != 0)
            panAcceleration.x += localpanAcceleration.x * Time.deltaTime * AccelerationUnit;
        else
        {
            panAcceleration.x *= decelerationFactor;
            if (panAcceleration.x < 0.01f && panAcceleration.x > -0.01f)
                panAcceleration.x = 0f;
        }
        
        // Debug.Log("Pan Acceleration: " + panAcceleration);

        panAcceleration.x = Mathf.Clamp(panAcceleration.x, -maxAcceleration, maxAcceleration);
        panAcceleration.y = Mathf.Clamp(panAcceleration.y, -maxAcceleration, maxAcceleration);

        // Debug.Log("Clamped Pan Acceleration: " + panAcceleration);

        pos.z += panAcceleration.y * delta;
        pos.x += panAcceleration.x * delta;

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
        if (_state != State.ZoomedIn) yield break;
        if (Draggable.DraggingThis != null) yield break;  // drag in progress, abort
        var nearest = ZoneCameraAnchor.FindClosestTo(transform.position);
        if (nearest != null)
            TransitionTo(nearest.transform.position, nearest.transform.rotation,
                        () => _state = State.ZoomedIn);
    }

    bool HasZoomedPanInput()
    {
        return Input.mousePosition.y >= Screen.height - panBorderThickness ||
            Input.mousePosition.y <= panBorderThickness ||
            Input.mousePosition.x >= Screen.width - panBorderThickness ||
            Input.mousePosition.x <= panBorderThickness ||
            Input.GetKey("w") || Input.GetKey("s") ||
            Input.GetKey("d") || Input.GetKey("a");
    }

}
