using UnityEngine;
using System.Collections.Generic;

public class ZonePath : MonoBehaviour
{
    [SerializeField] private ZoneManager _zoneA;
    [SerializeField] private ZoneManager _zoneB;
    [SerializeField] private float _lateralThreshold = 0.5f;

    public ZonePathLogic Logic { get; private set; }
    public ZoneManager ZoneA => _zoneA;
    public ZoneManager ZoneB => _zoneB;

    public static Dictionary<int, ZonePath> AllPaths { get; } = new Dictionary<int, ZonePath>();

    private PathVisual _visual;

    void Awake()
    {
        _visual = GetComponent<PathVisual>();
    }

    void Start()
    {
        if (_zoneA == null || _zoneB == null)
        {
            Debug.LogError($"[ZonePath] {name}: ZoneA ou ZoneB non assigné.", this);
            return;
        }
        if (_zoneA.transform.position.z > _zoneB.transform.position.z)
            (_zoneA, _zoneB) = (_zoneB, _zoneA);

        int id = _zoneA.Logic.ID ^ _zoneB.Logic.ID;
        Logic = new ZonePathLogic(id, _zoneA.Logic, _zoneB.Logic);

        float deltaZ = _zoneA.transform.position.z - _zoneB.transform.position.z;
        Logic.SetLateral(Mathf.Abs(deltaZ) < _lateralThreshold);

        _zoneA.RegisterPath(this);
        _zoneB.RegisterPath(this);

        AllPaths[id] = this;

        _visual?.Initialize(this);

    }

    void OnDestroy()
    {
        if (Logic != null)
            AllPaths.Remove(Logic.ID);
        _zoneA?.UnregisterPath(this);
        _zoneB?.UnregisterPath(this);
    }
}
