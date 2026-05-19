using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class ZoneManager : MonoBehaviour, ITargetableVisual, IPointerClickHandler
{
    [Header("Targeting")]
    [SerializeField] private Image _targetableOverlay;

    [HideInInspector]
    public List<PlayerArea> subZones = new List<PlayerArea>();

    private readonly List<ZonePath> _registeredPaths = new();

    public ZoneLogic Logic { get; private set; }

    public static List<ZoneManager> AllZones { get; } = new List<ZoneManager>();

    void Awake()
    {
        AllZones.Add(this);
        int id = GetHierarchyPath(transform).GetHashCode();
        Logic = new ZoneLogic(id, name, () => subZones.Select(sz => sz.baseID).ToList());

        foreach (PlayerArea pa in GetComponentsInChildren<PlayerArea>())
        {
            subZones.Add(pa);
            pa.parentZone = this;
        }

        if (_targetableOverlay != null)
            _targetableOverlay.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        AllZones.Remove(this);
    }

    public void RegisterPath(ZonePath path)
    {
        if (!_registeredPaths.Contains(path))
        {
            _registeredPaths.Add(path);
            Logic.AddPath(path.Logic);
        }
    }

    public void UnregisterPath(ZonePath path)
    {
        _registeredPaths.Remove(path);
        Logic.RemovePath(path.Logic);
    }

    public ZonePath GetPathTo(ZoneManager other)
        => _registeredPaths.Find(p =>
            (p.ZoneA == this && p.ZoneB == other) ||
            (p.ZoneA == other && p.ZoneB == this));

    public bool IsAdjacentTo(ZoneManager other) => GetPathTo(other) != null;

    public void UpdateTargetableVisual(bool targetable, bool targeted = false)
    {
        if (_targetableOverlay == null) return;
        _targetableOverlay.gameObject.SetActive(targetable);
        _targetableOverlay.color = targeted
            ? new Color(0f, 1f, 0f, 0.3f)
            : new Color(1f, 1f, 0f, 0.15f);
    }

    public void ClearTargetableVisual()
    {
        if (_targetableOverlay != null)
            _targetableOverlay.gameObject.SetActive(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (TurnManager.Instance == null) return;
        if (PhaseEffectPipeline.IsComplete) return;
        PhaseEffectPipeline.OnEntityClicked(Logic);
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
