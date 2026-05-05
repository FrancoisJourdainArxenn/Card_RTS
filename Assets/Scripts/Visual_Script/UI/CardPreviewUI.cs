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
    private RectTransform _anchorRect;
    private Canvas _canvas;

    void Awake()
    {
        Instance = this;
        _anchorRect = previewAnchor as RectTransform;
        _canvas = previewAnchor.GetComponentInParent<Canvas>();
    }

    public void Show(CardAsset asset, Vector2 mouseOffset)
    {
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

        GameObject prefab = GetPrefab(asset);

        if (currentPreview != null && prefab != currentPrefab)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }

        if (currentPreview == null)
        {
            currentPreview = Instantiate(prefab, previewAnchor);
            currentPreview.transform.localPosition = Vector3.zero;
            currentPreview.transform.localRotation = Quaternion.Euler(-90f , 0f, 0f);
            currentPrefab = prefab;
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

    static GameObject GetPrefab(CardAsset asset)
    {
        if (asset.MaxHealth > 0)
            return GlobalSettings.Instance.CreatureCardPrefab;
        if (asset.Targets == TargetingOptions.NoTarget)
            return GlobalSettings.Instance.NoTargetSpellCardPrefab;
        return GlobalSettings.Instance.TargetedSpellCardPrefab;
    }
}
