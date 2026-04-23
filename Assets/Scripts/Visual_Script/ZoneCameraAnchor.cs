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
            if (d < bestDist)
            {
                bestDist = d;
                best = anchor;
            }
        }
        return best;
    }

    public static ZoneCameraAnchor FindClosestFollowingDirection(Vector3 originalPosition, Vector3 direction)
    {
        ZoneCameraAnchor best = null;
        float bestDot = 0;
        float bestDist = float.MaxValue;
        const float dotEpsilon = 0.05f;
        direction.y = 0;
        direction.Normalize();
        foreach (var anchor in All)
        {
            Vector3 toAnchor = anchor.transform.position - originalPosition;
            toAnchor.y = 0;
            if (toAnchor.sqrMagnitude < 0.0001f) continue;
            float dist = toAnchor.magnitude;
            float dot = Vector3.Dot(toAnchor / dist, direction);
            bool clearlyBetter = dot > bestDot + dotEpsilon;
            bool tiedButCloser = dot > bestDot - dotEpsilon && dist < bestDist;
            if (clearlyBetter || tiedButCloser)
            {
                bestDot = dot;
                bestDist = dist;
                best = anchor;
            }
        }
        return best;
    }


    public void SetHighlighted(bool on)
    {
        if (_highlightRoot != null) _highlightRoot.SetActive(on);
    }

}
