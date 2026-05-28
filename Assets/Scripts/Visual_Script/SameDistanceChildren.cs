using UnityEngine;
using System.Collections;

// place first and last elements in children array manually
// others will be placed automatically with equal distances between first and last elements
public class SameDistanceChildren : MonoBehaviour {

    public Transform[] Children;

	// Use this for initialization
	void Awake ()
    {
        Vector3 firstElementPos = Children[0].transform.position;
        Vector3 lastElementPos = Children[Children.Length - 1].transform.position;

        // N children = N-1 segments between first and last
        Vector3 step = (lastElementPos - firstElementPos) / (Children.Length - 1);

        for (int i = 1; i < Children.Length - 1; i++)
        {
            Children[i].transform.position = firstElementPos + step * i;
        }
	}
}
