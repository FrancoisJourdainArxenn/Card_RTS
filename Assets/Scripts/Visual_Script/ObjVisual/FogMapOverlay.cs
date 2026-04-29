using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(MeshRenderer))]
public class FogMapOverlay : MonoBehaviour
{
    public static FogMapOverlay Instance { get; private set; }

    [SerializeField] int textureWidth  = 256;
    [SerializeField] int textureHeight = 256;
    [SerializeField] float revealDuration = 0.6f;
    [SerializeField] float coverDuration  = 0.35f;
    private Vector2 mapWorldMin;
    private Vector2 mapWorldSize;
    private Texture2D fogTex;
    private float[]   fogData; // [0..1] par pixel, CPU-side
    private Material  mat;

    // Une coroutine active par zone (ZoneID → coroutine)
    private Dictionary<int, Coroutine> activeAnims = new Dictionary<int, Coroutine>();

    void Awake()
    {
        Instance = this;
        MeshRenderer mr = GetComponent<MeshRenderer>();
        mr.sortingLayerName = "FogOverlay"; // le layer que tu crées dans Project Settings
        mr.sortingOrder = 999;


        fogData = new float[textureWidth * textureHeight]; // tout à 0 = brouillard

        fogTex = new Texture2D(textureWidth, textureHeight, TextureFormat.RFloat, false);
        fogTex.filterMode = FilterMode.Bilinear;
        UploadTexture();

        mat = GetComponent<MeshRenderer>().material;
        mat.SetTexture("_FogTex", fogTex);
        ComputeMapBounds();
    }

    // ─── API publique ────────────────────────────────────────────────────────

    public void SetZoneFoggedInstant(ZoneLogic zone, bool fogged)
    {
        StopZoneAnim(zone.ZoneID);
        RectInt bounds = GetZonePixelBounds(zone);
        float value = fogged ? 0f : 1f;
        PaintRegionFlat(bounds, value);
        UploadTexture();
    }

    public void RevealZoneAnimated(ZoneLogic zone, Vector3 originWorldPos)
    {
        StopZoneAnim(zone.ZoneID);
        RectInt  bounds    = GetZonePixelBounds(zone);
        float[]  distances = ComputeDistances(bounds, originWorldPos);
        Coroutine c = StartCoroutine(AnimateZone(zone.ZoneID, bounds, distances, false, revealDuration));
        activeAnims[zone.ZoneID] = c;
    }

    public void CoverZoneAnimated(ZoneLogic zone, Vector3 originWorldPos)
    {
        StopZoneAnim(zone.ZoneID);
        RectInt  bounds    = GetZonePixelBounds(zone);
        float[]  distances = ComputeDistances(bounds, originWorldPos);
        Coroutine c = StartCoroutine(AnimateZone(zone.ZoneID, bounds, distances, true, coverDuration));
        activeAnims[zone.ZoneID] = c;
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private void ComputeMapBounds()
    {
        ZoneLogic[] allZones = FindObjectsByType<ZoneLogic>(FindObjectsSortMode.None);
        if (allZones.Length == 0) return;

        Bounds total = GetZoneBounds(allZones[0]);
        for (int i = 1; i < allZones.Length; i++)
            total.Encapsulate(GetZoneBounds(allZones[i]));

        mapWorldMin  = new Vector2(total.min.x, total.min.z);
        mapWorldSize = new Vector2(total.size.x, total.size.z);

        mat.SetVector("_MapMin",  new Vector4(mapWorldMin.x,  mapWorldMin.y,  0, 0));
        mat.SetVector("_MapSize", new Vector4(mapWorldSize.x, mapWorldSize.y, 0, 0));
        Debug.Log($"MapMin={mapWorldMin} MapSize={mapWorldSize}");
    }
    
    // Conversion position monde → pixel dans la texture
    private Vector2Int WorldToPixel(Vector3 worldPos)
    {
        float u = Mathf.Clamp01((worldPos.x - mapWorldMin.x) / mapWorldSize.x);
        float v = Mathf.Clamp01((worldPos.z - mapWorldMin.y) / mapWorldSize.y);
        return new Vector2Int(
            Mathf.RoundToInt(u * (textureWidth  - 1)),
            Mathf.RoundToInt(v * (textureHeight - 1))
        );
    }


    // Calcule les bounds pixel d'une zone à partir de son Collider ou Renderer
    private RectInt GetZonePixelBounds(ZoneLogic zone)
    {
        Bounds b = GetZoneBounds(zone);
        Debug.Log($"Zone {zone.name} bounds: {b.min} → {b.max}");  // ← debug
        Vector2Int pMin = WorldToPixel(b.min);
        Vector2Int pMax = WorldToPixel(b.max);
        // Assure l'ordre correct (min < max)
        int xMin = Mathf.Min(pMin.x, pMax.x);
        int yMin = Mathf.Min(pMin.y, pMax.y);
        int xMax = Mathf.Max(pMin.x, pMax.x);
        int yMax = Mathf.Max(pMin.y, pMax.y);
        Debug.Log($"Zone {zone.name} pixel bounds: xMin={xMin} yMin={yMin} xMax={xMax} yMax={yMax}");

        return new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
    }

    private static Bounds GetZoneBounds(ZoneLogic zone)
    {
        // Collider direct sur la zone
        Collider col = zone.GetComponent<Collider>();
        if (col != null) return col.bounds;

        // Encapsule tous les Colliders enfants (TableVisual a un BoxCollider)
        Bounds bounds = new Bounds(zone.transform.position, Vector3.zero);
        bool found = false;
        foreach (Collider c in zone.GetComponentsInChildren<Collider>())
        {
            bounds.Encapsulate(c.bounds);
            found = true;
        }
        if (found) return bounds;

        // Fallback : positions des PlayerAreas
        foreach (PlayerArea pa in zone.subZones)
            bounds.Encapsulate(pa.transform.position);
        return bounds;
    }
    // Calcule pour chaque pixel de la région sa distance normalisée depuis l'origine
    private float[] ComputeDistances(RectInt bounds, Vector3 originWorldPos)
    {
        Vector2Int originPx = WorldToPixel(originWorldPos);
        float[] dists = new float[bounds.width * bounds.height];
        float maxDist = 0f;

        for (int y = 0; y < bounds.height; y++)
        for (int x = 0; x < bounds.width;  x++)
        {
            float d = Vector2.Distance(
                new Vector2(bounds.x + x, bounds.y + y),
                new Vector2(originPx.x,   originPx.y));
            dists[y * bounds.width + x] = d;
            if (d > maxDist) maxDist = d;
        }

        if (maxDist > 0f)
            for (int i = 0; i < dists.Length; i++)
                dists[i] /= maxDist;

        return dists;
    }

    private IEnumerator AnimateZone(int zoneID, RectInt bounds, float[] distances,
                                    bool covering, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float timer = Mathf.Clamp01(elapsed / duration);

            for (int y = 0; y < bounds.height; y++)
            for (int x = 0; x < bounds.width;  x++)
            {
                float dist  = distances[y * bounds.width + x];
                // Un pixel est révélé quand timer > dist (avec bord doux)
                float value = Mathf.Clamp01((timer - dist) / 0.08f + 0.5f);
                if (covering) value = 1f - value;

                int texIdx = (bounds.y + y) * textureWidth + (bounds.x + x);
                if (texIdx >= 0 && texIdx < fogData.Length)
                    fogData[texIdx] = value;
            }

            UploadTexture();
            yield return null;
        }

        // État final propre
        PaintRegionFlat(bounds, covering ? 0f : 1f);
        UploadTexture();
        activeAnims.Remove(zoneID);
    }

    private void PaintRegionFlat(RectInt bounds, float value)
    {
        for (int y = bounds.y; y < bounds.y + bounds.height; y++)
        for (int x = bounds.x; x < bounds.x + bounds.width;  x++)
        {
            int idx = y * textureWidth + x;
            if (idx >= 0 && idx < fogData.Length)
                fogData[idx] = value;
        }
    }

    private void UploadTexture()
    {
        // Convertit float[] en Color[] pour SetPixels
        Color[] colors = new Color[fogData.Length];
        for (int i = 0; i < fogData.Length; i++)
            colors[i] = new Color(fogData[i], 0, 0, 1);
        fogTex.SetPixels(colors);
        fogTex.Apply(false);
    }

    private void StopZoneAnim(int zoneID)
    {
        if (activeAnims.TryGetValue(zoneID, out Coroutine c) && c != null)
            StopCoroutine(c);
        activeAnims.Remove(zoneID);
    }

    void OnDestroy()
    {
        if (fogTex != null) Destroy(fogTex);
    }
}
