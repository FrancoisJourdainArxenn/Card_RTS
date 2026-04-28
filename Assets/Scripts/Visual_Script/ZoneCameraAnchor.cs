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

    public static List<ZoneCameraAnchor> FindInRadius(Vector3 originalPosition, float radius)
    {
        List<ZoneCameraAnchor> inRadius = new List<ZoneCameraAnchor>();
        foreach (var anchor in All)
        {
            float d = Vector2.Distance(
                new Vector2(anchor.transform.position.x, anchor.transform.position.z),
                new Vector2(originalPosition.x, originalPosition.z));
            if (d <= radius)
            {
                inRadius.Add(anchor);
            }
        }
        return inRadius;
    }


    public List<(ZoneCameraAnchor anchor, float angleDeg)> FindOthersWithAngle(float? maxDistance = null)
    {
        var result = new List<(ZoneCameraAnchor, float)>();
        foreach (var anchor in All)
        {
            if (anchor == this) continue;
            Vector3 dir = anchor.transform.position - transform.position;
            float dist = new Vector2(dir.x, dir.z).magnitude;
            if (maxDistance.HasValue && dist > maxDistance.Value) continue;
            float angleDeg = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            result.Add((anchor, angleDeg));
        }
        return result;
    }

    public List<(ZoneCameraAnchor anchor, float angleDeg)> FindClosestWithAngle(float minAngleDeg = 30, float? maxDistance = null)
    {
        var result = new List<(ZoneCameraAnchor anchor, float angleDeg, float dist)>();
        foreach (var anchor in All)
        {
            if (anchor == this) continue;
            Vector3 dir = anchor.transform.position - transform.position;
            float dist = new Vector2(dir.x, dir.z).magnitude;
            if (maxDistance.HasValue && dist > maxDistance.Value) continue;
            float angleDeg = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;

            int conflict = -1;
            for (int i = 0; i < result.Count; i++)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(angleDeg, result[i].angleDeg)) < minAngleDeg)
                {
                    conflict = i;
                    break;
                }
            }

            if (conflict == -1)
                result.Add((anchor, angleDeg, dist));
            else if (dist < result[conflict].dist)
                result[conflict] = (anchor, angleDeg, dist);
        }

        return result.ConvertAll(e => (e.anchor, e.angleDeg));
    }

    public void SetHighlighted(bool on)
    {
        if (BuildingShopVisual.IsOpen)
            return;
        if (_highlightRoot != null) _highlightRoot.SetActive(on);
    }

}
