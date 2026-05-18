using System.Collections.Generic;
using UnityEngine;

public class PathObstacle : MonoBehaviour, IPathBlocker
{
    [SerializeField] private ZonePath _path;
    [SerializeField] private ObstacleAsset _asset;

    public static Dictionary<int, PathObstacle> All { get; } = new Dictionary<int, PathObstacle>();

    public int ID { get; private set; }
    public int Health { get; set; }

    public bool IsActive { get; private set; } = true;
    public event System.Action OnDeactivated;

    private int _turnsRemaining;

    public void Initialize(ZonePath path, ObstacleAsset asset)
    {
        _path = path;
        _asset = asset;
        Setup();
    }

    void Start()
    {
        if (_path != null && _asset != null)
            Setup();
    }

    private void Setup()
    {
        IDHolder id = GetComponent<IDHolder>() ?? gameObject.AddComponent<IDHolder>();
        ID = id.UniqueID;
        All[ID] = this;

        if (_asset.type == ObstacleAsset.BlockerType.Destructible)
            Health = _asset.maxHealth;
        else
        {
            _turnsRemaining = _asset.duration;
            TurnManager.OnRoundStart += OnTurnStart;
        }

        _path.Logic.SetBlocker(this);
    }

    public void TakeDamage(int damage)
    {
        if (_asset.type != ObstacleAsset.BlockerType.Destructible) return;
        Health = Mathf.Max(0, Health - damage);
        if (Health == 0) Destroy(gameObject);
    }

    private void OnTurnStart()
    {
        if (--_turnsRemaining <= 0)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        All.Remove(ID);
        TurnManager.OnRoundStart -= OnTurnStart;
        if (!IsActive) return;
        IsActive = false;
        OnDeactivated?.Invoke();
    }
}
