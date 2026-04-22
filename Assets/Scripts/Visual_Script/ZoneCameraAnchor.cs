using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class ZoneCameraAnchor : MonoBehaviour
{
    public static readonly List<ZoneCameraAnchor> All = new();
    [SerializeField] GameObject _highlightRoot;

    void OnEnable()  => All.Add(this);
    void OnDisable() => All.Remove(this);

    public static ZoneCameraAnchor FindClosestTo(Vector3 worldPos)
    {
        ZoneCameraAnchor best = null;
        float bestDist = float.MaxValue;
        foreach (var anchor in All)
        {
            float d = Vector2.Distance(
                new Vector2(anchor.transform.position.x, anchor.transform.position.z),
                new Vector2(worldPos.x, worldPos.z));
            if (d < bestDist) { bestDist = d; best = anchor; }
        }
        return best;
    }


    public void SetHighlighted(bool on)
    {
        if (_highlightRoot != null) _highlightRoot.SetActive(on);
    }

}
