using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class ZoneManager : MonoBehaviour, ITargetableVisual, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
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
        if (targetable)
        {
            _targetableOverlay.gameObject.SetActive(true);
            Canvas parentCanvas = _targetableOverlay.GetComponentInParent<Canvas>(true);
            if (parentCanvas != null) parentCanvas.gameObject.SetActive(true);
        }
        _targetableOverlay.enabled = targetable;
        _targetableOverlay.color = targeted
            ? new Color(0f, 1f, 0f, 1f)
            : new Color(1f, 1f, 0f, 0.3f);
    }

    public void ClearTargetableVisual()
    {
        if (_targetableOverlay != null)
        {
            _targetableOverlay.enabled = false;
            _targetableOverlay.gameObject.SetActive(false);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ScanButton.HandleZoneClickIfActive(this, eventData)) return;
        if (TurnManager.Instance == null) return;
        if (PhaseEffectPipeline.IsComplete) return;
        PhaseEffectPipeline.OnEntityClicked(Logic);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!ScanButton.IsActive) return;
        if (_targetableOverlay == null) return;
        bool noPresence = FogOfWarManager.Instance == null || FogOfWarManager.Instance.IsZoneFogged(this);
        if (!noPresence) return;
        _targetableOverlay.gameObject.SetActive(true);
        _targetableOverlay.enabled = true;
        _targetableOverlay.color = new Color(1f, 1f, 0f, 1f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!ScanButton.IsActive) return;
        bool noPresence = FogOfWarManager.Instance == null || FogOfWarManager.Instance.IsZoneFogged(this);
        if (!noPresence) return;
        UpdateTargetableVisual(true);
    }

    void LateUpdate()
    {
        if (!ScanButton.IsActive) return;
        if (_targetableOverlay == null) return;
        bool shouldBeVisible = FogOfWarManager.Instance == null || FogOfWarManager.Instance.IsZoneFogged(this);
        if (shouldBeVisible && !_targetableOverlay.enabled)
            Debug.LogWarning($"{name}: overlay désactivé pendant le scan — canvas actif={_targetableOverlay.GetComponentInParent<Canvas>(true)?.gameObject.activeInHierarchy}", this);
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
