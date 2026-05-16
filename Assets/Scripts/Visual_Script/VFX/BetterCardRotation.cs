using UnityEngine;
using System.Collections;

/// <summary>
/// This script should be attached to the card game object to display card`s rotation correctly.
/// </summary>

[ExecuteInEditMode]
public class BetterCardRotation : MonoBehaviour {

    // parent game object for all the card face graphics
    public RectTransform CardFront;

    // parent game object for all the card back graphics
    public RectTransform CardBack;

    // an empty game object that is placed a bit above the face of the card, in the center of the card
    public Transform targetFacePoint;

    // 3d collider attached to the card (2d colliders like BoxCollider2D won`t work in this case)
    public Collider col;

    // if this is true, our players currently see the card Back
    private bool showingBack = false;

	// Update is called once per frame
	void Update ()
    {
        Vector3 dir = targetFacePoint.position - Camera.main.transform.position;
        Ray ray = new Ray(Camera.main.transform.position, dir.normalized);
        bool passedThroughColliderOnCard = col.Raycast(ray, out _, dir.magnitude);

        if (passedThroughColliderOnCard != showingBack)
        {
            showingBack = passedThroughColliderOnCard;
            CardFront.gameObject.SetActive(!showingBack);
            CardBack.gameObject.SetActive(showingBack);
        }
	}
}
