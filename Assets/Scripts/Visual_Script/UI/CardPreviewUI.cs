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

    void Awake()
    {
        Instance = this;
    }

    public void Show(CardAsset asset)
    {
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
        ReminderTextManager.Instance?.ShowTooltips(asset.Keywords);

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
        if (asset.Effects[0].RequiresPlayerInput)
            return GlobalSettings.Instance.NoTargetSpellCardPrefab;
        return GlobalSettings.Instance.TargetedSpellCardPrefab;
    }
}
