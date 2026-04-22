using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

public class CameraController : MonoBehaviour
{
    public float panSpeed = 20f;
    public float panBorderThickness = 10f;
    public Vector2 panLimit;
    private Vector2 panAcceleration = Vector2.zero;
    public float minAcceleration = 0f;
    public float maxAcceleration = 1f;

    [Header("Zone lock")]
    public float transitionDuration = 0.5f;
    [Range(0f, 50f)]
    public float zoomedInPanSpeed = 5f;
    public Ease transitionEase = Ease.InOutQuad;

    enum State { Overhead, Transitioning, ZoneLocked }
    State _state = State.Overhead;
    Vector3 _savedOverheadPosition;
    Quaternion _savedOverheadRotation;

    ZoneCameraAnchor _hoveredAnchor;


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

        if (_state == State.ZoneLocked)
        {
            if (Input.GetAxis("Mouse ScrollWheel") < 0f)
                TransitionTo(_savedOverheadPosition, _savedOverheadRotation,
                             () => _state = State.Overhead);
            if (TurnManager.Instance != null && TurnManager.Instance.IsCommandPhase)
            {
                HandlePan(zoomedInPanSpeed);
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
                TransitionTo(anchor.transform.position, anchor.transform.rotation,
                             () => _state = State.ZoneLocked);
            }
        }
    }

    void HandlePan(float speed, Vector2? clampLimit = null)
    {
        Vector3 pos = transform.position;
        float delta = speed * Time.deltaTime;
        Vector2 localpanAcceleration = Vector2.zero;
        localpanAcceleration.y += Input.mousePosition.y - (Screen.height - panBorderThickness);
        localpanAcceleration.x += Input.mousePosition.x - (Screen.width - panBorderThickness);

        if (localpanAcceleration.y > 0 || localpanAcceleration.y < panBorderThickness)
            panAcceleration.y += localpanAcceleration.y * Time.deltaTime /100;
        else
            panAcceleration.y -= 1f * Time.deltaTime /100;

        if (localpanAcceleration.x > 0 || localpanAcceleration.x < panBorderThickness)
            panAcceleration.x += localpanAcceleration.x * Time.deltaTime /100;
        else
            panAcceleration.x -= 1f * Time.deltaTime /100;
        panAcceleration.x = Mathf.Clamp(panAcceleration.x, minAcceleration, maxAcceleration);
        panAcceleration.y = Mathf.Clamp(panAcceleration.y, minAcceleration, maxAcceleration);

        pos.z += panAcceleration.y * delta;
        pos.x += panAcceleration.x * delta;
        /*
        if (Input.GetKey("z") || Input.mousePosition.y >= Screen.height - panBorderThickness)
        {
            pos.z += panAcceleration.y * delta;
        }
        if (Input.GetKey("s") || Input.mousePosition.y <= panBorderThickness)
        {
            
        }                 pos.z -= delta;
        if (Input.GetKey("d") || Input.mousePosition.x >= Screen.width  - panBorderThickness) pos.x += delta;
        if (Input.GetKey("q") || Input.mousePosition.x <= panBorderThickness)                 pos.x -= delta;
        */
        if (clampLimit.HasValue)
        {
            pos.x = Mathf.Clamp(pos.x, -clampLimit.Value.x, clampLimit.Value.x);
            pos.z = Mathf.Clamp(pos.z, -clampLimit.Value.y, clampLimit.Value.y);
        }

        // if (Input.GetAxis("Horizontal") == 0f && panAcceleration.x > 0f)
        // {
        //     panAcceleration.x -=1f;
        // }
        // if (Input.GetAxis("Vertical") == 0f && panAcceleration.y > 0f)
        // {
        //     panAcceleration.y -=1f;
        // }
        // panAcceleration.x += Input.GetAxis("Horizontal");
        // panAcceleration.y += Input.GetAxis("Vertical");
        // panAcceleration.x = Mathf.Clamp(panAcceleration.x, minAcceleration, maxAcceleration);
        // panAcceleration.y = Mathf.Clamp(panAcceleration.y, minAcceleration, maxAcceleration);
        // zoomedInPanSpeed = panSpeed * panAcceleration.magnitude * Time.deltaTime;*/

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
}
