using UnityEngine;
using TMPro;

public class UITooltip : MonoBehaviour
{
    public static UITooltip Instance { get; private set; }
    [SerializeField] private RectTransform canvasRectTransform;
    [SerializeField] private Vector2 padding = new Vector2(8f, 8f); // Padding around the text
    [SerializeField] private float maxWidth = 400f;

    private Canvas canvas;
    private TextMeshProUGUI tooltipText;
    private RectTransform backgroundRectTransform;
    private RectTransform rectTransform;

    private void Awake()
    {
        Instance = this;
        canvas = canvasRectTransform.GetComponent<Canvas>();
        backgroundRectTransform = transform.Find("background_UITooltip").GetComponent<RectTransform>();
        tooltipText = transform.Find("Text_UITooltip").GetComponent<TextMeshProUGUI>();
        rectTransform = GetComponent<RectTransform>();

        HideToolTip();
    }

    private void SetText(string tooltipTextToSet)
    {
        RectTransform textRect = tooltipText.rectTransform;
        textRect.sizeDelta = new Vector2(maxWidth, textRect.sizeDelta.y);

        tooltipText.SetText(tooltipTextToSet);
        tooltipText.ForceMeshUpdate();

        Vector2 textSize = tooltipText.GetRenderedValues(false);
        textSize.x = Mathf.Min(textSize.x, maxWidth);

        backgroundRectTransform.sizeDelta = textSize + padding;
    }

    private void Update()
    {
        rectTransform.anchoredPosition = Input.mousePosition / canvasRectTransform.localScale.x;

        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 shift = ComputeScreenShift(backgroundRectTransform, cam);
        if (shift != Vector2.zero)
            rectTransform.anchoredPosition += shift;
    }

    private Vector2 ComputeScreenShift(RectTransform rect, Camera cam)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners); // 0 = bas-gauche, 2 = haut-droite

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        float shiftX = 0f;
        if (min.x < 0f) shiftX = -min.x;
        else if (max.x > Screen.width) shiftX = Screen.width - max.x;

        float shiftY = 0f;
        if (min.y < 0f) shiftY = -min.y;
        else if (max.y > Screen.height) shiftY = Screen.height - max.y;

        return new Vector2(shiftX, shiftY) / canvas.scaleFactor;
    }


    private void ShowToolTip(string tooltipTextToShow)
    {
        gameObject.SetActive(true);
        SetText(tooltipTextToShow);
    }

    private void HideToolTip()
    {
        gameObject.SetActive(false);
    }

    // private void SHowTooltip(System.Func<string> getTooltipTextFunc)
    // {
    //     this.getTooltipTextFunc = getTooltipTextFunc;
    //     gameObject.SetActive(true);
    //     SetText(getTooltipTextFunc());
    // }

    public static void ShowTooltip_Static(string tooltipTextToShow)
    {
        Instance.ShowToolTip(tooltipTextToShow);
    }

    public static void ShowTooltip_Static(System.Func<string> tooltipTextToShow)
    {
        Instance.ShowToolTip(tooltipTextToShow());
    }

    public static void HideTooltip_Static()
    {
        Instance.HideToolTip();
    }

}
