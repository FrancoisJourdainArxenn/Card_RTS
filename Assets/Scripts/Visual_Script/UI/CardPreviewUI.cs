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
    [SerializeField] private GameObject effectTriggeredPrefab;
    private RectTransform _anchorRect;
    private Canvas _canvas;
    private bool previewingEffects = false;

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
    private List<GameObject> _autoEffectPreviews     = new List<GameObject>();
    private List<GameObject> _opponentEffectPreviews = new List<GameObject>();
    private Coroutine _autoEffectDismissCoroutine;
    [SerializeField] private float autoEffectDisplayDuration = 3f;

    void Awake()
    {
        Instance = this;
        _anchorRect = previewAnchor as RectTransform;
        _canvas = previewAnchor.GetComponentInParent<Canvas>();
    }

    void OnEnable()
    {
        TargetingVisualEvents.OnTargetingStarted         += HandleTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded           += HandleTargetingEnded;
        TargetingVisualEvents.OnAutoEffectTriggered      += HandleAutoEffect;
        TargetingVisualEvents.OnEffectsExecuting         += ClearOpponentStack;
        TargetingVisualEvents.OnOpponentTargetingStarted += HandleOpponentTargetingStarted;
        TargetingVisualEvents.OnOpponentTargetingEnded   += HandleOpponentTargetingEnded;
    }

    void OnDisable()
    {
        TargetingVisualEvents.OnTargetingStarted         -= HandleTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded           -= HandleTargetingEnded;
        TargetingVisualEvents.OnAutoEffectTriggered      -= HandleAutoEffect;
        TargetingVisualEvents.OnEffectsExecuting         -= ClearOpponentStack;
        TargetingVisualEvents.OnOpponentTargetingStarted -= HandleOpponentTargetingStarted;
        TargetingVisualEvents.OnOpponentTargetingEnded   -= HandleOpponentTargetingEnded;
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
        if (!IsEffectVisibleToLocalPlayer(context)) return;
        if (_targetingPreviews.Count > 0) return;

        CardAsset ca = (context.Source as CreatureLogic)?.ca
                    ?? (context.Source as BuildingLogic)?.ca;
        if (ca == null) return;

        int sourceID = (context.Source as CreatureLogic)?.UniqueCreatureID
                    ?? (context.Source as BuildingLogic)?.UniqueBuildingID
                    ?? -1;

        PushToAutoStack(new PendingEffectSelection
        {
            Data            = data,
            Context         = context,
            EligibleTargets = new List<IIdentifiable>(),
            SourceEntityID  = sourceID
        });
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
        // _opponentEffectPreviews persiste jusqu'à OnEffectsExecuting → ClearOpponentStack()
        if (_opponentEffectPreviews.Count == 0)
            previewingEffects = false;
    }

    public void Show(CardAsset asset, Vector2 mouseOffset)
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

        ShowPreview(asset, previewPosition);
    }

    private void ShowPreview(CardAsset asset, Vector2 previewPosition)
    {
        if (cardPreviewPrefab == null) return;

        if (currentPreview != null && currentPrefab != cardPreviewPrefab)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }

        if (currentPreview == null)
        {
            currentPreview = Instantiate(cardPreviewPrefab, previewAnchor);
            currentPreview.transform.localPosition = Vector3.zero;
            currentPreview.transform.localRotation = Quaternion.identity;
            currentPrefab = cardPreviewPrefab;
        }

        OneCardManager manager = currentPreview.GetComponent<OneCardManager>();
        manager.cardAsset = asset;
        manager.ReadCardFromAsset();
        if (ReminderTextManager.Instance != null)
            ReminderTextManager.Instance.ShowTooltips(asset.Keywords, previewPosition);

        currentPreview.SetActive(true);
        currentPreview.transform.localScale = Vector3.one * previewScale * 0.5f;
        currentPreview.transform.DOScale(Vector3.one * previewScale, 0.3f).SetEase(Ease.OutBack);
    }

    private void HandleOpponentTargetingStarted(int[] sourceEntityIDs, int[] effectIndexes)
    {
        ClearOpponentStack();

        for (int i = 0; i < sourceEntityIDs.Length; i++)
        {
            int sourceEntityID = sourceEntityIDs[i];
            int effectIndex    = effectIndexes[i];

            CreatureLogic.CreaturesCreatedThisGame.TryGetValue(sourceEntityID, out CreatureLogic creature);
            BuildingLogic.BuildingsCreatedThisGame.TryGetValue(sourceEntityID, out BuildingLogic building);

            CardAsset ca = creature != null ? creature.ca : building != null ? building.ca : null;
            if (ca == null || ca.Effects == null || effectIndex < 0 || effectIndex >= ca.Effects.Count) continue;

            Player caster   = creature != null ? creature.owner : building != null ? building.owner : null;
            ILivable source = creature != null ? (ILivable)creature : (ILivable)building;
            EffectContext context = new EffectContext { Caster = caster, Source = source };

            if (!IsEffectVisibleToLocalPlayer(context)) continue;

            PendingEffectSelection selection = new PendingEffectSelection
            {
                Data              = ca.Effects[effectIndex],
                Context           = context,
                EligibleTargets   = new List<IIdentifiable>(),
                SourceEntityID    = sourceEntityID,
                EffectIndexInCard = effectIndex
            };

            GameObject preview = CreateTargetingPreview(selection);
            if (preview == null) continue;

            preview.transform.localPosition = Vector3.zero;
            preview.transform.localScale    = Vector3.one * previewScale * stackDisplayScale * 0.5f;
            _opponentEffectPreviews.Add(preview);

            GameObject sourceGO = IDHolder.GetGameObjectWithID(sourceEntityID);
            if (sourceGO != null) SpawnTrail(sourceGO.transform);
        }

        RefreshAllStacks();
    }

    private void HandleOpponentTargetingEnded() => ClearOpponentStack();

    private void ClearOpponentStack()
    {
        foreach (GameObject go in _opponentEffectPreviews)
            if (go != null) Destroy(go);
        _opponentEffectPreviews.Clear();
        if (_targetingPreviews.Count == 0 && _autoEffectPreviews.Count == 0)
            previewingEffects = false;
    }

    private void PushToAutoStack(PendingEffectSelection selection)
    {
        if (_autoEffectDismissCoroutine != null)
            StopCoroutine(_autoEffectDismissCoroutine);

        GameObject preview = CreateTargetingPreview(selection);
        if (preview == null) return;

        preview.transform.localPosition = Vector3.zero;
        preview.transform.localScale    = Vector3.one * previewScale * stackDisplayScale * 0.5f;
        _autoEffectPreviews.Insert(0, preview);

        GameObject sourceGO = IDHolder.GetGameObjectWithID(selection.SourceEntityID);
        if (sourceGO != null) SpawnTrail(sourceGO.transform);

        RefreshAllStacks();

        _autoEffectDismissCoroutine = StartCoroutine(AutoDismissStack());
    }

    private void RefreshAllStacks()
    {
        var combined = new List<GameObject>();
        combined.AddRange(_targetingPreviews);
        combined.AddRange(_opponentEffectPreviews);
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
        yield return new WaitForSeconds(autoEffectDisplayDuration);
        ClearAutoStack();
    }


    public void Hide()
    {
        ReminderTextManager.Instance?.HideTooltips();
        if (currentPreview != null)
            currentPreview.SetActive(false);
    }

}
