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
    [SerializeField] private float stackScaleFactor = 0.85f;
    [SerializeField] private float stackOffsetX     = 15f;
    [SerializeField] private float stackOffsetY     = -15f;

    [Header("Trail")]
    [SerializeField] private GameObject trailPrefab;
    [SerializeField] private Transform trailTarget;
    [SerializeField] private float stackAppearDelay = 0.5f;
    public float StackAppearDelay => stackAppearDelay;


    [Header("Hover Arrow")]
    [SerializeField] private HoverArrow hoverArrow;
    [SerializeField] private Transform arrowEndPoint;

    private List<GameObject> _targetingPreviews = new List<GameObject>();

    void Awake()
    {
        Instance = this;
        _anchorRect = previewAnchor as RectTransform;
        _canvas = previewAnchor.GetComponentInParent<Canvas>();
    }

    void OnEnable()
    {
        TargetingVisualEvents.OnTargetingStarted += HandleTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded   += HandleTargetingEnded;
    }

    void OnDisable()
    {
        TargetingVisualEvents.OnTargetingStarted -= HandleTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded   -= HandleTargetingEnded;
    }

    private void HandleTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
    {
        int remaining = queue.Count - currentIndex;

        if (_targetingPreviews.Count == 0 && remaining > 0)
        {
            StartCoroutine(BuildStackDelayed(queue, currentIndex, remaining));
        }
        else if (_targetingPreviews.Count > remaining)
        {
            PopFront();
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

    private void BuildStack(List<PendingEffectSelection> queue, int currentIndex, int remaining)
    {
        // Create back cards first so front ends up as the top sibling
        for (int i = remaining - 1; i >= 0; i--)
        {
            GameObject preview = CreateTargetingPreview(queue[currentIndex + i]);
            if (preview == null) continue;

            _targetingPreviews.Insert(0, preview);

            float scale = previewScale * Mathf.Pow(stackScaleFactor, i);
            preview.transform.localPosition = StackPosition(i);
            preview.transform.localScale    = Vector3.one * scale;
        }

        if (_targetingPreviews.Count > 0)
        {
            _targetingPreviews[0].transform.localScale = Vector3.one * previewScale * 0.5f;
            _targetingPreviews[0].transform.DOScale(Vector3.one * previewScale, 0.3f).SetEase(Ease.OutBack);
        }
    }

    private void PopFront()
    {
        if (_targetingPreviews.Count == 0) return;

        Destroy(_targetingPreviews[0]);
        _targetingPreviews.RemoveAt(0);

        for (int i = 0; i < _targetingPreviews.Count; i++)
        {
            float   scale     = previewScale * Mathf.Pow(stackScaleFactor, i);
            Vector3 targetPos = StackPosition(i);
            _targetingPreviews[i].transform.DOLocalMove(targetPos, 0.3f).SetEase(Ease.OutQuad);
            _targetingPreviews[i].transform.DOScale(
                Vector3.one * scale, 0.3f).SetEase(i == 0 ? Ease.OutBack : Ease.OutQuad);
        }
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
        hoverArrow?.Hide();
        foreach (GameObject preview in _targetingPreviews)
            if (preview != null) Destroy(preview);
        _targetingPreviews.Clear();
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


    public void Hide()
    {
        ReminderTextManager.Instance?.HideTooltips();
        if (currentPreview != null)
            currentPreview.SetActive(false);
    }

}
