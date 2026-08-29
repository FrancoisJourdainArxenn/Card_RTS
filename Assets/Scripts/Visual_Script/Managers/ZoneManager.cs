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
        // Animator.StringToHash (CRC32) au lieu de string.GetHashCode() : ce dernier est randomisé
        // par processus (.NET hash-code randomization) — la même zone obtenait un ID différent sur
        // le host et sur chaque client, cassant toute résolution d'ID de zone reçu par le réseau
        // (voir PhaseEffectPipeline.ResolveEntityByID) alors que la même hiérarchie de scène produit
        // le même chemin des deux côtés.
        int id = Animator.StringToHash(GetHierarchyPath(transform));
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

    public ZonePath GetPathTo(ZoneManager other, Player player, bool isFlying = false)
    {
        ZonePath fallback = null;
        foreach (ZonePath p in _registeredPaths)
        {
            bool connects = (p.ZoneA == this && p.ZoneB == other) || (p.ZoneA == other && p.ZoneB == this);
            if (!connects) continue;
            if (p.Logic.RequiresFlying && !isFlying) continue;

            fallback ??= p;
            if (p.Logic.CanTraverse(player, Logic, isFlying))
                return p;
        }
        return fallback;
    }

    public bool IsAdjacentTo(ZoneManager other) => _registeredPaths.Exists(p =>
        (p.ZoneA == this && p.ZoneB == other) || (p.ZoneA == other && p.ZoneB == this));

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
        if (OnPlayTargetingSession.IsActive && eventData.button == PointerEventData.InputButton.Right)
        {
            OnPlayTargetingSession.Cancel();
            return;
        }

        if (ScanButton.HandleZoneClickIfActive(this, eventData)) return;
        if (TurnManager.Instance == null) return;
        if (!OnPlayTargetingSession.IsActive && PhaseEffectPipeline.IsComplete) return;
        PhaseEffectPipeline.OnEntityClicked(Logic);
    }

    // Vaut aussi pour une carte dont l'OnPlay cible une Zone (ex: RevealZoneSO) : sans ces
    // workarounds — écrits à l'origine seulement pour Scan —, l'overlay/canvas d'une zone jamais
    // vue (typiquement la zone contenant la base principale adverse) reste inerte au clic pendant
    // une session de ciblage générique (OnPlayTargetingSession), alors que Scan y arrivait déjà.
    private static bool IsTargetingFoggedZones => ScanButton.IsActive || OnPlayTargetingSession.IsActive;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsTargetingFoggedZones) return;
        if (_targetableOverlay == null) return;
        bool noPresence = FogOfWarManager.Instance == null || FogOfWarManager.Instance.IsZoneFogged(this);
        if (!noPresence) return;
        _targetableOverlay.gameObject.SetActive(true);
        _targetableOverlay.enabled = true;
        _targetableOverlay.color = new Color(1f, 1f, 0f, 1f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!IsTargetingFoggedZones) return;
        bool noPresence = FogOfWarManager.Instance == null || FogOfWarManager.Instance.IsZoneFogged(this);
        if (!noPresence) return;
        UpdateTargetableVisual(true);
    }

    void LateUpdate()
    {
        if (!IsTargetingFoggedZones) return;
        if (_targetableOverlay == null) return;
        bool shouldBeVisible = FogOfWarManager.Instance == null || FogOfWarManager.Instance.IsZoneFogged(this);
        // if (shouldBeVisible && !_targetableOverlay.enabled)
            // Debug.LogWarning($"{name}: overlay désactivé pendant le ciblage — canvas actif={_targetableOverlay.GetComponentInParent<Canvas>(true)?.gameObject.activeInHierarchy}", this);
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
