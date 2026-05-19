using UnityEngine;

[DefaultExecutionOrder(-100)]
public class MapLoader : MonoBehaviour
{
    public static Transform EnvironnementTransform { get; private set; }
    public static MapLoader Instance { get; private set; }

    [SerializeField] GameObject defaultMapPrefab;
    [SerializeField] GameObject[] mapPrefabs;

    void Awake()
    {
        Instance = this;
        EnvironnementTransform = transform;
        if (!NetworkSessionData.IsNetworkSession)
            Instantiate(defaultMapPrefab, transform.position, transform.rotation, transform);
    }

    public GameObject GetMapPrefab(int index) => mapPrefabs[index];
}


