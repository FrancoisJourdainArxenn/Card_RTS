using UnityEngine;

public class MapManager : MonoBehaviour
{
    [Header("Camera")]
    public float cameraHeight = 50f;
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

        NeutralBases = GetComponentsInChildren<NeutralZoneController>();

        PlayerArea[] all = GetComponentsInChildren<PlayerArea>();
        TopPlayerAreas    = System.Array.FindAll(all, a => a.owner == AreaPosition.Top);
        LowPlayerAreas    = System.Array.FindAll(all, a => a.owner == AreaPosition.Low);
    }


}

