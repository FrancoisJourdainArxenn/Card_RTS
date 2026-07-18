using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class CardPreviewUI : MonoBehaviour
{
    public static CardPreviewUI Instance { get; private set; }
    [Header("Preview Settings")]
    public float previewScale = 1f;

    public Transform previewAnchor;

    private GameObject currentPreview;
    private GameObject currentPrefab;
    [SerializeField] private GameObject cardPreviewPrefab;
    [SerializeField] private GameObject heroCardPreviewPrefab;
    [SerializeField] private GameObject effectTriggeredPrefab;
    private RectTransform _anchorRect;
    private Canvas _canvas;
    private bool previewingEffects = false;

    [SerializeField] private Material _opponentHologramMaterial;

    [SerializeField] private RectTransform targetingAnchor;
    [SerializeField] private float stackScaleFactor  = 0.85f;
    [SerializeField] private float stackDisplayScale = 0.75f;
    [SerializeField] private float stackOffsetX      = 15f;
    [SerializeField] private float stackOffsetY      = -15f;

    [Header("Trail")]
    [SerializeField] private GameObject trailPrefab;
    [SerializeField] private Transform trailTarget;
    [SerializeField] private float stackAppearDelay = 0.5f;
    public float StackAppearDelay => stackAppearDelay;


    [Header("Hover Arrow")]
    [SerializeField] private HoverArrow hoverArrow;
    [SerializeField] private Transform arrowEndPoint;

    private List<GameObject> _targetingPreviews  = new List<GameObject>();
    private List<GameObject> _autoEffectPreviews = new List<GameObject>();
    private Coroutine _autoEffectDismissCoroutine;
    private bool _batchActive = false;
    [SerializeField] private float autoEffectDisplayDuration = 3f;

    void Awake()
    {
        Instance = this;
        _anchorRect = previewAnchor as RectTransform;
        _canvas = previewAnchor.GetComponentInParent<Canvas>();
    }

    void OnEnable()
    {
        TargetingVisualEvents.OnTargetingStarted    += HandleTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded      += HandleTargetingEnded;
        TargetingVisualEvents.OnAutoEffectTriggered += HandleAutoEffect;
        TargetingVisualEvents.OnEffectsBatchPending  += HandleEffectsBatch;
        TargetingVisualEvents.OnEffectResolved       += HandleEffectResolved;
        TargetingVisualEvents.OnEffectsBatchComplete += HandleEffectsBatchComplete;
    }

    void OnDisable()
    {
        TargetingVisualEvents.OnTargetingStarted    -= HandleTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded      -= HandleTargetingEnded;
        TargetingVisualEvents.OnAutoEffectTriggered -= HandleAutoEffect;
        TargetingVisualEvents.OnEffectsBatchPending  -= HandleEffectsBatch;
        TargetingVisualEvents.OnEffectResolved       -= HandleEffectResolved;
        TargetingVisualEvents.OnEffectsBatchComplete -= HandleEffectsBatchComplete;
    }

    private ZoneLogic GetCreatureZone(CreatureLogic creature)
    {
        foreach (PlayerArea pa in creature.owner.PAreas)
            if (pa.baseID == creature.BaseID)
                return pa.parentZone.Logic;
        return null;
    }
    
    private bool IsEntityZoneVisible(ILivable entity, List<ZoneLogic> visibleZones)
    {
        if (entity == null) return false;
        ZoneLogic zone = entity switch
        {
            CreatureLogic c => GetCreatureZone(c),
            BuildingLogic b => b.OriginSpot.Zone.Logic,
            BaseLogic     b => b.Zone,
            _               => null
        };
        return zone != null && visibleZones.Contains(zone);
    }

    
    private bool IsEffectVisibleToLocalPlayer(EffectContext context)
    {
        Player local = GlobalSettings.Instance.localPlayer;
        if (context.Caster == local) return true;

        List<ZoneLogic> visible = local.VisibleZones;
        return IsEntityZoneVisible(context.Source, visible)
            || IsEntityZoneVisible(context.Target, visible)
            || (context.TargetedZone != null && visible.Contains(context.TargetedZone));
    }
    
    private void HandleTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
    {
        int remaining = queue.Count - currentIndex;

        if (queue.Count > currentIndex && !IsEffectVisibleToLocalPlayer(queue[currentIndex].Context))
            return;

        ClearAutoStack();

        if (_targetingPreviews.Count == 0 && remaining > 0)
        {
            StartCoroutine(BuildStackDelayed(queue, currentIndex, remaining));
        }
        else if (_targetingPreviews.Count > remaining)
        {
            PopFront();

            // Mettre à jour la source de la flèche pour le nouvel effet en tête
            if (remaining > 0 && hoverArrow != null && arrowEndPoint != null)
            {
                GameObject newFrontSourceGO = IDHolder.GetGameObjectWithID(queue[currentIndex].SourceEntityID);
                if (newFrontSourceGO != null)
                    hoverArrow.Show(newFrontSourceGO.transform, arrowEndPoint);
            }
        }
        // equal count = target selected/deselected, no stack change
    }

    private IEnumerator BuildStackDelayed(List<PendingEffectSelection> queue, int currentIndex, int remaining)
    {
        for (int i = 0; i < remaining; i++)
        {
            GameObject sourceGO = IDHolder.GetGameObjectWithID(queue[currentIndex + i].SourceEntityID);
            if (sourceGO != null)
                SpawnTrail(sourceGO.transform);
        }
        
        yield return new WaitForSeconds(stackAppearDelay);

        GameObject frontSourceGO = IDHolder.GetGameObjectWithID(queue[currentIndex].SourceEntityID);
        if (hoverArrow != null && frontSourceGO != null && arrowEndPoint != null)
            hoverArrow.Show(frontSourceGO.transform, arrowEndPoint);

        BuildStack(queue, currentIndex, remaining);
    }

    private void HandleAutoEffect(CardEffectData data, EffectContext context)
    {
        if (_batchActive) return;
        if (!IsEffectVisibleToLocalPlayer(context)) return;
        if (_targetingPreviews.Count > 0) return;

        CardAsset ca = (context.Source as CreatureLogic)?.ca
                    ?? (context.Source as BuildingLogic)?.ca;
        if (ca == null) return;

        int sourceID = (context.Source as CreatureLogic)?.UniqueCreatureID
                    ?? (context.Source as BuildingLogic)?.UniqueBuildingID
                    ?? -1;

        Material mat = context.Caster != GlobalSettings.Instance.localPlayer
            ? _opponentHologramMaterial
            : null;

        PushToAutoStack(new PendingEffectSelection
        {
            Data            = data,
            Context         = context,
            EligibleTargets = new List<IIdentifiable>(),
            SourceEntityID  = sourceID
        }, mat);
    }


    private void HandleEffectsBatch(List<PendingEffectSelection> effects, List<bool> hasVisuals)
    {
        _batchActive = true;
        if (_targetingPreviews.Count > 0) return;

        ClearAutoStack();

        for (int i = 0; i < effects.Count; i++)
        {
            if (hasVisuals != null && i < hasVisuals.Count && !hasVisuals[i]) continue;
            if (!IsEffectVisibleToLocalPlayer(effects[i].Context)) continue;

            GameObject preview = CreateTargetingPreview(effects[i]);
            if (preview == null) continue;

            Material mat = effects[i].Context.Caster != GlobalSettings.Instance.localPlayer
                ? _opponentHologramMaterial : null;
            if (mat != null) ApplyMaterialToHologram(preview, mat);

            preview.transform.localPosition = Vector3.zero;
            preview.transform.localScale    = Vector3.one * previewScale * stackDisplayScale * 0.5f;
            _autoEffectPreviews.Add(preview);

            GameObject sourceGO = IDHolder.GetGameObjectWithID(effects[i].SourceEntityID);
            if (sourceGO != null) SpawnTrail(sourceGO.transform);
        }
        RefreshAllStacks();
    }

    private void HandleEffectResolved()
    {
        if (_autoEffectPreviews.Count == 0) return;
        GameObject front = _autoEffectPreviews[0];
        _autoEffectPreviews.RemoveAt(0);
        if (front != null) Destroy(front);
        RefreshAllStacks();
    }

    private void HandleEffectsBatchComplete()
    {
        _batchActive = false;
        if (_autoEffectPreviews.Count == 0 && _targetingPreviews.Count == 0)
            previewingEffects = false;
    }

    private void BuildStack(List<PendingEffectSelection> queue, int currentIndex, int remaining)
    {
        for (int i = remaining - 1; i >= 0; i--)
        {
            GameObject preview = CreateTargetingPreview(queue[currentIndex + i]);
            if (preview == null) continue;
            _targetingPreviews.Insert(0, preview);
            preview.transform.localPosition = Vector3.zero;
            preview.transform.localScale    = Vector3.one * previewScale * stackDisplayScale * 0.5f;
        }
        RefreshAllStacks();
    }

    private void PopFront()
    {
        if (_targetingPreviews.Count == 0) return;
        Destroy(_targetingPreviews[0]);
        _targetingPreviews.RemoveAt(0);
        RefreshAllStacks();
    }

    private Vector3 StackPosition(int index)
    {
        float x = 0f, y = 0f;
        for (int j = 0; j < index; j++)
        {
            float stepScale = Mathf.Pow(stackScaleFactor, j);
            x += stackOffsetX * stepScale;
            y += stackOffsetY * stepScale;
        }
        return new Vector3(x, y, 0f);
    }

    private void SpawnTrail(Transform origin)
    {
        if (trailPrefab == null || trailTarget == null) return;
        CameraController cam = Camera.main.GetComponent<CameraController>();
        bool isZoomed = cam != null && cam.IsZoomedIn;
        TrailAnimator trail = Instantiate(trailPrefab).GetComponent<TrailAnimator>();
        trail.Play(origin, trailTarget, isZoomed);
    }

    private void ApplyMaterialToHologram(GameObject preview, Material material)
    {
        if (material == null) return;
        Transform hologram = preview.transform.Find("CardPanel/Hologram");
        if (hologram == null) return;
        UnityEngine.UI.Image img = hologram.GetComponent<UnityEngine.UI.Image>();
        if (img != null) img.material = material;
    }

    private GameObject CreateTargetingPreview(PendingEffectSelection selection)
    {
        GameObject sourceGO = IDHolder.GetGameObjectWithID(selection.SourceEntityID);
        if (sourceGO == null) return null;

        CardAsset cardAsset = sourceGO.GetComponent<OneCreatureManager>()?.cardAsset
                        ?? sourceGO.GetComponent<OneBuildingManager>()?.cardAsset;
        if (cardAsset == null) return null;

        if (effectTriggeredPrefab == null) return null;

        GameObject preview = Instantiate(effectTriggeredPrefab, targetingAnchor);
        preview.transform.localPosition = Vector3.zero;
        preview.transform.localRotation = Quaternion.identity;

        OneCardManager manager = preview.GetComponent<OneCardManager>();
        manager.cardAsset = cardAsset;
        manager.ReadEffectFromAsset(selection.Data.EffectName);
        previewingEffects = true;
        preview.SetActive(true);
        return preview;
    }


    private void HandleTargetingEnded()
    {
        StopAllCoroutines();
        _autoEffectDismissCoroutine = null;
        hoverArrow?.Hide();
        foreach (GameObject preview in _targetingPreviews)
            if (preview != null) Destroy(preview);
        _targetingPreviews.Clear();
        foreach (GameObject go in _autoEffectPreviews)
            if (go != null) Destroy(go);
        _autoEffectPreviews.Clear();
        previewingEffects = false;
    }

    public void Show(CardAsset asset, Vector2 mouseOffset, Player owner = null, int? attackOverride = null, int? healthOverride = null, int? maxHealthOverride = null)
    {
        if(previewingEffects)
            return;
        Camera uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(),
            Input.mousePosition,
            uiCamera,
            out Vector2 localPoint
        );
        Vector2 previewPosition = localPoint + mouseOffset;
        _anchorRect.anchoredPosition = previewPosition;

        ShowPreview(asset, previewPosition, owner, attackOverride, healthOverride, maxHealthOverride);
    }

    private void ShowPreview(CardAsset asset, Vector2 previewPosition, Player owner = null, int? attackOverride = null, int? healthOverride = null, int? maxHealthOverride = null)
    {
        GameObject prefabToUse = (asset.IsHero && heroCardPreviewPrefab != null) ? heroCardPreviewPrefab : cardPreviewPrefab;
        if (prefabToUse == null) return;

        if (currentPreview != null && currentPrefab != prefabToUse)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }

        if (currentPreview == null)
        {
            currentPreview = Instantiate(prefabToUse, previewAnchor);
            currentPreview.transform.localPosition = Vector3.zero;
            currentPreview.transform.localRotation = Quaternion.identity;
            currentPrefab = prefabToUse;
        }

        OneCardManager manager = currentPreview.GetComponent<OneCardManager>();
        manager.cardAsset = asset;
        manager.owner = owner;
        manager.ReadCardFromAsset();
        manager.OverrideStats(attackOverride, healthOverride, maxHealthOverride);
        if (ReminderTextManager.Instance != null)
            ReminderTextManager.Instance.ShowTooltips(asset.Keywords, previewPosition);

        currentPreview.SetActive(true);
        currentPreview.transform.localScale = Vector3.one * previewScale * 0.5f;
        currentPreview.transform.DOScale(Vector3.one * previewScale, 0.3f).SetEase(Ease.OutBack);
    }

    private void PushToAutoStack(PendingEffectSelection selection, Material materialOverride = null)
    {
        GameObject preview = CreateTargetingPreview(selection);
        if (preview == null) return;

        if (materialOverride != null)
            ApplyMaterialToHologram(preview, materialOverride);

        preview.transform.localPosition = Vector3.zero;
        preview.transform.localScale    = Vector3.one * previewScale * stackDisplayScale * 0.5f;
        _autoEffectPreviews.Add(preview);

        GameObject sourceGO = IDHolder.GetGameObjectWithID(selection.SourceEntityID);
        if (sourceGO != null) SpawnTrail(sourceGO.transform);

        RefreshAllStacks();

        if (_autoEffectDismissCoroutine == null)
            _autoEffectDismissCoroutine = StartCoroutine(AutoDismissStack());
    }

    private void RefreshAllStacks()
    {
        var combined = new List<GameObject>();
        combined.AddRange(_targetingPreviews);
        combined.AddRange(_autoEffectPreviews);

        for (int i = 0; i < combined.Count; i++)
        {
            if (combined[i] == null) continue;
            float scale = previewScale * stackDisplayScale * Mathf.Pow(stackScaleFactor, i);
            combined[i].transform.DOLocalMove(StackPosition(i), 0.3f).SetEase(Ease.OutQuad);
            combined[i].transform.DOScale(
                Vector3.one * scale, 0.3f).SetEase(i == 0 ? Ease.OutBack : Ease.OutQuad);
        }

        // Sibling order : du fond vers l'avant, l'index 0 se retrouve en dernière position = rendu au-dessus
        for (int i = combined.Count - 1; i >= 0; i--)
            if (combined[i] != null) combined[i].transform.SetAsLastSibling();
    }

    private void ClearAutoStack()
    {
        if (_autoEffectDismissCoroutine != null)
        {
            StopCoroutine(_autoEffectDismissCoroutine);
            _autoEffectDismissCoroutine = null;
        }
        foreach (GameObject go in _autoEffectPreviews)
            if (go != null) Destroy(go);
        _autoEffectPreviews.Clear();
        if (_targetingPreviews.Count == 0)
            previewingEffects = false;
    }

    private IEnumerator AutoDismissStack()
    {
        while (_autoEffectPreviews.Count > 0)
        {
            yield return new WaitForSeconds(autoEffectDisplayDuration);

            if (_autoEffectPreviews.Count == 0) break;

            GameObject front = _autoEffectPreviews[0];
            _autoEffectPreviews.RemoveAt(0);
            if (front != null) Destroy(front);

            RefreshAllStacks();
        }

        if (_targetingPreviews.Count == 0)
            previewingEffects = false;
        _autoEffectDismissCoroutine = null;
    }


    public void Hide()
    {
        ReminderTextManager.Instance?.HideTooltips();
        if (currentPreview != null)
            currentPreview.SetActive(false);
    }

}
