using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

[RequireComponent(typeof(RectTransform))]
public class MenuCardPreviewUI : MonoBehaviour
{
    public static MenuCardPreviewUI Instance { get; private set; }

    [SerializeField] private Vector2 previewOffset = new(250f, 0f);
    [SerializeField] private float previewScale = 1f;

    [Header("Mêmes prefabs que CardPreviewUI en BattleScene")]
    [SerializeField] private GameObject unitCardPreviewPrefab;   // Card_Unit_Preview (fallback Unit/Building)
    [SerializeField] private GameObject heroCardPreviewPrefab;   // Hero_Preview
    [SerializeField] private GameObject actionCardPreviewPrefab; // Card_Action_Preview

    [Header("Row keywords (mêmes assets que GlobalSettings en BattleScene)")]
    [SerializeField] private Keyword meleeRowKeyword;  // Melee.asset
    [SerializeField] private Keyword rangedRowKeyword; // Ranged.asset

    [Header("Row icons (mêmes assets que GlobalSettings en BattleScene)")]
    [SerializeField] private Sprite meleeIcon;
    [SerializeField] private Sprite rangedIcon;

    private GameObject currentPreview;
    private GameObject currentPrefab;
    private RectTransform _anchorRect;
    private Canvas _canvas;

    void Awake()
    {
        Instance = this;
        _anchorRect = (RectTransform)transform;
        _canvas = GetComponentInParent<Canvas>();
    }

    public void Show(CardAsset asset)
    {
        if (asset == null) return;

        _anchorRect.SetAsLastSibling(); // toujours au-dessus du panel DeckList

        Camera uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(),
            Input.mousePosition,
            uiCamera,
            out Vector2 localPoint
        );
        _anchorRect.anchoredPosition = localPoint + previewOffset;

        ShowPreview(asset);
    }

    private void ShowPreview(CardAsset asset)
    {
        GameObject prefabToUse = (asset.IsHero && heroCardPreviewPrefab != null) ? heroCardPreviewPrefab
            : (asset.Type == CardType.Action && actionCardPreviewPrefab != null) ? actionCardPreviewPrefab
            : unitCardPreviewPrefab;
        if (prefabToUse == null) return;

        if (currentPreview != null && currentPrefab != prefabToUse)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }

        if (currentPreview == null)
        {
            currentPreview = Instantiate(prefabToUse, _anchorRect);
            currentPreview.transform.localPosition = Vector3.zero;
            currentPreview.transform.localRotation = Quaternion.identity;
            currentPrefab = prefabToUse;

            // La preview est positionnee pres/sous la souris et passee au premier plan (SetAsLastSibling) :
            // sans ca, ses Graphics internes (raycastTarget=true sur les images/textes du prefab) volent
            // le raycast au DeckList et a ses entrees des que la preview les recouvre, ce qui declenche
            // un faux OnPointerExit et ferme le panneau alors que la souris n'a pas bouge du DeckList.
            CanvasGroup previewCanvasGroup = currentPreview.GetComponent<CanvasGroup>();
            if (previewCanvasGroup == null)
                previewCanvasGroup = currentPreview.AddComponent<CanvasGroup>();
            previewCanvasGroup.blocksRaycasts = false;
            previewCanvasGroup.interactable = false;
        }

        OneCardManager manager = currentPreview.GetComponent<OneCardManager>();
        manager.cardAsset = asset;
        manager.owner = null;
        manager.sourceCreature = null;
        manager.sourceBuilding = null;
        manager.ReadCardFromAsset();
        manager.OverrideStats(null, null, null);

        List<Keyword> keywords = new List<Keyword>(asset.Keywords);
        if (asset.Type == CardType.Unit)
        {
            Keyword rowKeyword = asset.melee ? meleeRowKeyword : rangedRowKeyword;
            if (rowKeyword != null)
                keywords.Insert(0, rowKeyword);

            // GlobalSettings (BattleScene) n'existe pas ici, donc OneCardManager.ReadCardFromAsset
            // n'a pas pu mettre a jour RowIcon (condition GlobalSettings.Instance != null) : on le
            // fait nous-memes avec les sprites locaux, meme logique que les Keyword ci-dessus.
            if (manager.RowIcon != null)
                manager.RowIcon.sprite = asset.melee ? meleeIcon : rangedIcon;
        }
        ReminderTextManager.Instance?.BuildTooltips(keywords);
        CardTooltipManager.Instance?.BuildCardTooltips(asset.ReferencedCards);

        currentPreview.SetActive(true);

        // mesurer/clamp à la taille finale (carte + tooltips), puis repartir de l'échelle réduite pour l'animation de pop-in
        currentPreview.transform.localScale = Vector3.one * previewScale;
        ClampPreviewToScreen((RectTransform)currentPreview.transform);

        ReminderTextManager.Instance?.AnchorToCard(_anchorRect.anchoredPosition);
        CardTooltipManager.Instance?.AnchorToCard(_anchorRect.anchoredPosition);
        ReminderTextManager.Instance?.FadeIn();
        CardTooltipManager.Instance?.FadeIn();

        currentPreview.transform.localScale = Vector3.one * previewScale * 0.5f;
        currentPreview.transform.DOKill();
        currentPreview.transform.DOScale(Vector3.one * previewScale, 0.3f).SetEase(Ease.OutBack);
    }

    private void ClampPreviewToScreen(RectTransform previewRect)
    {
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        Vector2 shift = ComputeScreenShift(previewRect, cam);

        ReminderTextManager reminders = ReminderTextManager.Instance;
        if (reminders != null && reminders.TooltipRect.childCount > 0)
        {
            RectTransform tooltipRect = reminders.TooltipRect;
            Vector2 savedTooltipPos = tooltipRect.anchoredPosition;

            tooltipRect.anchoredPosition = _anchorRect.anchoredPosition + shift + reminders.TooltipOffset;
            shift += ComputeScreenShift(tooltipRect, cam);

            tooltipRect.anchoredPosition = savedTooltipPos;
        }

        CardTooltipManager cardTooltips = CardTooltipManager.Instance;
        if (cardTooltips != null && cardTooltips.TooltipRect.childCount > 0)
        {
            RectTransform cardTooltipRect = cardTooltips.TooltipRect;
            Vector2 savedPos = cardTooltipRect.anchoredPosition;

            cardTooltips.AnchorToCard(_anchorRect.anchoredPosition + shift);
            shift += ComputeScreenShift(cardTooltipRect, cam);

            cardTooltipRect.anchoredPosition = savedPos;
        }

        if (shift != Vector2.zero)
            _anchorRect.anchoredPosition += shift;
    }

    private Vector2 ComputeScreenShift(RectTransform rect, Camera cam)
    {
        Vector2 min, max;
        GetScreenBounds(rect, cam, out min, out max);

        float shiftX = 0f;
        if (min.x < 0f) shiftX = -min.x;
        else if (max.x > Screen.width) shiftX = Screen.width - max.x;

        float shiftY = 0f;
        if (min.y < 0f) shiftY = -min.y;
        else if (max.y > Screen.height) shiftY = Screen.height - max.y;

        return new Vector2(shiftX, shiftY) / _canvas.scaleFactor;
    }

    // ReminderTextManager/CardTooltipManager gardent une largeur de conteneur figée (ContentSizeFitter
    // horizontal = Unconstrained) pendant que leur HorizontalLayoutGroup/VerticalLayoutGroup positionne
    // des enfants (panneaux de mots-clés, mini-cartes) qui débordent largement de cette largeur — donc
    // on mesure l'étendue réelle en incluant les enfants, pas seulement le rect (figé) du conteneur.
    private void GetScreenBounds(RectTransform rect, Camera cam, out Vector2 min, out Vector2 max)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        for (int i = 0; i < rect.childCount; i++)
        {
            if (rect.GetChild(i) is not RectTransform child) continue;

            child.GetWorldCorners(corners);
            Vector2 childMin = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 childMax = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            min = Vector2.Min(min, childMin);
            max = Vector2.Max(max, childMax);
        }
    }

    public void Hide()
    {
        ReminderTextManager.Instance?.HideTooltips();
        CardTooltipManager.Instance?.HideTooltips();
        if (currentPreview != null)
            currentPreview.SetActive(false);
    }
}
