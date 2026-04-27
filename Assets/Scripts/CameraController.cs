using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

public class CameraController : MonoBehaviour
{
    public float panBorderThicknessVertical = 50f;
    public float panBorderThicknessHorizontal = 50f;

    [Header("Zone lock")]
    [Range(0f, 2f)]
    public float transitionDuration = 0.7f;
    public Ease transitionEase = Ease.InOutCubic;

    enum State { Overhead, Transitioning, ZoomedIn }
    State _state = State.Overhead;
    Vector3 _savedOverheadPosition;
    Quaternion _savedOverheadRotation;

    ZoneCameraAnchor _hoveredAnchor;

    // void Start() => StartCoroutine(WaitForLocalPlayer());

    // IEnumerator WaitForLocalPlayer()
    // {
    //     yield return new WaitUntil(() =>
    //         GlobalSettings.Instance.localPlayer != null &&
    //         GlobalSettings.Instance.localPlayer.MainPArea != null);

    //     Vector3 basePos = GlobalSettings.Instance.localPlayer.MainPArea.transform.position;
    //     transform.position = new Vector3(basePos.x, 50f, basePos.z);
    // }

    void Update()
    {
        switch (_state)
        {
            case State.Transitioning:
                break;
            case State.ZoomedIn:
                HandleZoomedInPan();
                break;
            case State.Overhead:
                HandleZoomedOutPan();
                break;
        }
    }

    void HandleZoomedInPan()
    {
        if (Input.GetAxis("Mouse ScrollWheel") < 0f)
        {
            TransitionTo(
                _savedOverheadPosition,
                _savedOverheadRotation,
                () => _state = State.Overhead
            );
            return;
        }
        Vector3 direction = Vector3.zero;
        if (Input.mousePosition.y >= Screen.height - panBorderThicknessVertical || Input.GetKey("w"))
            direction.z += 1;
        if (Input.mousePosition.y <= panBorderThicknessVertical || Input.GetKey("s"))
            direction.z -= 1;
        if (Input.mousePosition.x >= Screen.width - panBorderThicknessHorizontal || Input.GetKey("d"))
            direction.x += 1;
        if (Input.mousePosition.x <= panBorderThicknessHorizontal || Input.GetKey("a"))
            direction.x -= 1;            

        if (direction == Vector3.zero)
            return;
                
        MoveCameraToClosestBase(transform.position, direction);
    }

    void HandleZoomedOutPan()
    {
        // Overhead state
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
                             () => _state = State.ZoomedIn);
            }
        }
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

    void MoveCameraToClosestBase(Vector3 pos, Vector3? direction = null)
    {
        var nearest = direction.HasValue
            ? ZoneCameraAnchor.FindClosestFollowingDirection(pos, direction.Value)
            : ZoneCameraAnchor.FindClosestTo(pos);
        if (nearest != null)
        {
            TransitionTo(
                nearest.transform.position,
                nearest.transform.rotation,
                () => _state = State.ZoomedIn
            );
        }
        else
        {
            // Debug.Log("No anchor nearby");
        }
    }
}
