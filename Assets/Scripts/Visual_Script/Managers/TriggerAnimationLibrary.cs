using UnityEngine;
using System.Collections.Generic;

public class TriggerAnimationLibrary : MonoBehaviour
{
    [System.Serializable]
    public class TriggerAnimationEntry
    {
        public TriggerType Trigger;
        public GameObject VfxPrefab;
    }

    public static TriggerAnimationLibrary Instance { get; private set; }

    [SerializeField] private List<TriggerAnimationEntry> entries = new List<TriggerAnimationEntry>();

    private Dictionary<TriggerType, GameObject> lookup;

    void Awake()
    {
        Instance = this;
        lookup = new Dictionary<TriggerType, GameObject>();
        foreach (TriggerAnimationEntry entry in entries)
        {
            if (entry.VfxPrefab != null)
                lookup[entry.Trigger] = entry.VfxPrefab;
        }
    }

    public GameObject GetVfxPrefab(TriggerType trigger)
    {
        lookup.TryGetValue(trigger, out GameObject prefab);
        return prefab;
    }
}
