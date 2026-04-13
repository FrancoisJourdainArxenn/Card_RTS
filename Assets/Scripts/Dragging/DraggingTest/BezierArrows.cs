using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class BezierArrows : MonoBehaviour
{
    public GameObject ArrowHeadPrefab;
    public GameObject ArrowNodePrefab;
    public int arrowNodeNum;
    public float scaleFactor = 1f;

    #region Private Fields
    private RectTransform origin;
    private List<RectTransform> arrowNodes = new List<RectTransform>();
    private List<Vector2> controlPoints = new List<Vector2>();
    private readonly List<Vector2> controlPointsFactors = new List<Vector2> { new Vector2(-0.3f, 0.8f), new Vector2(0.1f, 1.4f) };
    #endregion

    void Awake()
    {
        // Gets position of the arrows emitter point.
        this.origin = this.GetComponent<RectTransform>();

        // Instantiates the arrow nodes and arrow head.
        for (int i = 0; i < this.arrowNodeNum; ++i)
        {
            this.arrowNodes.Add(Instantiate(this.ArrowNodePrefab, this.transform).GetComponent<RectTransform>());
        }

        this.arrowNodes.Add(Instantiate(this.ArrowHeadPrefab, this.transform).GetComponent<RectTransform>());

        // Hides the arrow nodes.
        this.arrowNodes.ForEach(a => a.GetComponent<RectTransform>().position = new Vector2(-1000, -1000));

        // Initializes the control points list.
        for (int i = 0; i < 4; ++i)
        {
            this.controlPoints.Add(Vector2.zero);
        }
    }

    void Update()
    {
        // Set the first and last control points.
        this.controlPoints[0] = new Vector2(this.origin.position.x, this.origin.position.y);

        this.controlPoints[3] = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

        this.controlPoints[1] = this.controlPoints[0] + (this.controlPoints[3] - this.controlPoints[0]) * this.controlPointsFactors[0];
        this.controlPoints[2] = this.controlPoints[0] + (this.controlPoints[3] - this.controlPoints[0]) * this.controlPointsFactors[1];

        // Index Bezier curve
        for (int i = 0; i < this.arrowNodes.Count; ++i)
        {
            // Fix 1: added missing semicolon
            var t = Mathf.Log(1f * i / (this.arrowNodes.Count - 1) + 1f, 2f);
            this.arrowNodes[i].position =
                Mathf.Pow(1 - t, 3) * this.controlPoints[0] +
                3 * Mathf.Pow(1 - t, 2) * t * this.controlPoints[1] +
                3 * (1 - t) * Mathf.Pow(t, 2) * this.controlPoints[2] +
                Mathf.Pow(t, 3) * this.controlPoints[3];

            // Calculates rotations for each arrow node.
            if (i > 0)
            {
                // Fix 2: added missing comma, changed 'index' to 'i'
                var euler = new Vector3(0, 0, Vector2.SignedAngle(Vector2.up, this.arrowNodes[i].position - this.arrowNodes[i - 1].position));
                this.arrowNodes[i].rotation = Quaternion.Euler(euler);
            }

            var scale = this.scaleFactor * (1f - 0.03f * (this.arrowNodes.Count - 1 - i));
            this.arrowNodes[i].localScale = new Vector3(scale, scale, 1f);
        }

        // Fix 3: moved inside Update() — was incorrectly placed outside the method
        this.arrowNodes[0].transform.rotation = this.arrowNodes[1].transform.rotation;
    }
}
