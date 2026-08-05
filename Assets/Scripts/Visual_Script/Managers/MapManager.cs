using UnityEngine;

[DefaultExecutionOrder(-200)]
public class MapManager : MonoBehaviour
{
    [Header("Camera - Middle view")]
    public float cameraHeight = 50f;
    public bool clampMiddlePan = false;
    public Vector2 middlePanMin;
    public Vector2 middlePanMax;

    [Header("Camera - Top view (max zoom out)")]
    public float topHeight = 100f;
    public Vector2 topPanMin;
    public Vector2 topPanMax;

    public static MapManager Current { get; private set; }

    [Header("Player Base Spawns")]
    public Transform TopPlayerBaseSpawn;
    public Transform LowPlayerBaseSpawn;

    [Header("Map Content")]
    public NeutralZoneController[] NeutralBases;
    public GameObject MainBasePrefab;

    public PlayerArea[] TopPlayerAreas    { get; private set; }
    public PlayerArea[] LowPlayerAreas    { get; private set; }

    void Awake()
    {
        Current = this;

        NeutralBaseVisual[] neutralBaseVisuals = GetComponentsInChildren<NeutralBaseVisual>(true);
        for (int i = 0; i < neutralBaseVisuals.Length; i++)
            neutralBaseVisuals[i].SetId(i);

        NeutralBases = GetComponentsInChildren<NeutralZoneController>();

        PlayerArea[] all = GetComponentsInChildren<PlayerArea>();
        TopPlayerAreas    = System.Array.FindAll(all, a => a.owner == AreaPosition.Top);
        LowPlayerAreas    = System.Array.FindAll(all, a => a.owner == AreaPosition.Low);
    }


}

