using UnityEngine;
using UnityEngine.Serialization;

public class CameraController : MonoBehaviour
{
    public float panSpeed = 20f;
    public float scrollSpeed = 20f;
    public float minY = 20f;
    public float maxY = 120f;
    public float panBorderThickness = 10f;
    public Vector2 panLimit;

    [Header("Zoom & pan")]
    [Tooltip("Multiplicateur de pan quand la caméra est proche du sol (Y ≈ minY, zoom fort).")]
    [Range(0.05f, 2f)]
    [FormerlySerializedAs("panSpeedZoomedOut")]
    public float panScaleWhenZoomedIn = 0.5f;

    [Tooltip("Multiplicateur de pan quand la caméra est haute (Y ≈ maxY, dézoom).")]
    [Range(0.05f, 2f)]
    [FormerlySerializedAs("panSpeedZoomedIn")]
    public float panScaleWhenZoomedOut = 1f;

    void Update()
    {
        Vector3 pos = transform.position;

        // Y bas = zoom fort (pan plus lent), Y haut = dézoom (pan plus rapide).
        float zoomT = Mathf.InverseLerp(minY, maxY, pos.y);
        float panScale = Mathf.Lerp(panScaleWhenZoomedIn, panScaleWhenZoomedOut, zoomT);
        float effectivePan = panSpeed * panScale;

        if (Input.GetKey("w") || Input.mousePosition.y >= Screen.height - panBorderThickness)
            pos.z += effectivePan * Time.deltaTime;

        if (Input.GetKey("s") || Input.mousePosition.y <= panBorderThickness)
            pos.z -= effectivePan * Time.deltaTime;

        if (Input.GetKey("d") || Input.mousePosition.x >= Screen.width - panBorderThickness)
            pos.x += effectivePan * Time.deltaTime;

        if (Input.GetKey("q") || Input.mousePosition.x <= panBorderThickness)
            pos.x -= effectivePan * Time.deltaTime;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        pos.y -= scroll * scrollSpeed * 100f * Time.deltaTime;
        
        pos.x = Mathf.Clamp(pos.x, -panLimit.x, panLimit.x);
        pos.z = Mathf.Clamp(pos.z, -panLimit.y, panLimit.y);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        transform.position = pos;
    }
}
