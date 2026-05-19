using UnityEngine;

[DefaultExecutionOrder(-100)]
public class MapLoader : MonoBehaviour
{
    [SerializeField] GameObject defaultMapPrefab;
    [SerializeField] Transform environnement;

    void Awake()
    {
        if (!NetworkSessionData.IsNetworkSession)
            Instantiate(defaultMapPrefab, environnement.position, environnement.rotation, environnement);
    }
}

