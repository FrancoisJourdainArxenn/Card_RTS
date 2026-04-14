using System.Collections.Generic;
using UnityEngine;

public class BezierArrows : MonoBehaviour
{
    public GameObject ArrowHeadPrefab;
    public GameObject ArrowNodePrefab;
    public int arrowNodeNum;
    public float scaleFactor = 1f;

    #region Private Fields
    private Transform origin;
    private List<Transform> arrowNodes = new List<Transform>();
    private List<Vector3> controlPoints = new List<Vector3>();
    private readonly List<Vector2> controlPointsFactors = new List<Vector2>
    {
        new Vector2(-0.3f, 0.8f),
        new Vector2(0.1f, 0.85f)
    };
    #endregion

    void Awake()
    {
        this.origin = this.transform;

        for (int i = 0; i < this.arrowNodeNum; ++i)
            this.arrowNodes.Add(Instantiate(this.ArrowNodePrefab, this.transform).transform);

        this.arrowNodes.Add(Instantiate(this.ArrowHeadPrefab, this.transform).transform);

        // Hide nodes far away until dragging starts
        this.arrowNodes.ForEach(a => a.position = new Vector3(-9999f, -9999f, 0f));

        for (int i = 0; i < 4; ++i)
            this.controlPoints.Add(Vector3.zero);
    }

    void Update()
    {
        Vector3 mouseWorldPos = GetMouseWorldPosition();

        this.controlPoints[0] = this.origin.position;
        this.controlPoints[3] = mouseWorldPos;

        // Apply the curve factors to the XY delta; keep Z flat
        Vector3 delta = this.controlPoints[3] - this.controlPoints[0];
        this.controlPoints[1] = this.controlPoints[0] + new Vector3(
            delta.x * this.controlPointsFactors[0].x,
            0f,
            delta.z * this.controlPointsFactors[0].y);
        this.controlPoints[2] = this.controlPoints[0] + new Vector3(
            delta.x * this.controlPointsFactors[1].x,
            0f,
            delta.z * this.controlPointsFactors[1].y);

        for (int i = 0; i < this.arrowNodes.Count; ++i)
        {
            var t = Mathf.Log(1f * i / (this.arrowNodes.Count - 1) + 1f, 2f);
            this.arrowNodes[i].position =
                Mathf.Pow(1 - t, 3)         * this.controlPoints[0] +
                3 * Mathf.Pow(1 - t, 2) * t * this.controlPoints[1] +
                3 * (1 - t) * Mathf.Pow(t, 2) * this.controlPoints[2] +
                Mathf.Pow(t, 3)              * this.controlPoints[3];

            if (i > 0)
            {
                Vector3 dir = new Vector3(
                    this.arrowNodes[i].position.x - this.arrowNodes[i - 1].position.x,
                    0f,
                    this.arrowNodes[i].position.z - this.arrowNodes[i - 1].position.z).normalized;
                if (dir.sqrMagnitude > 0.0001f)
                    // LookRotation(Vector3.up, dir): local Z faces camera, local Y faces travel direction
                    this.arrowNodes[i].rotation = Quaternion.LookRotation(Vector3.up, dir);
            }

            var scale = this.scaleFactor * (1f - 0.03f * (this.arrowNodes.Count - 1 - i));
            this.arrowNodes[i].localScale = new Vector3(scale, scale, 1f);
        }

        this.arrowNodes[0].rotation = this.arrowNodes[1].rotation;
    }

    private Vector3 GetMouseWorldPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        // Plane perpendicular to Z axis at the same depth as the emitter
        Plane gamePlane = new Plane(Vector3.up, origin.position);
        if (gamePlane.Raycast(ray, out float distance))
            return ray.GetPoint(distance);
        return origin.position; // fallback
    }
}
