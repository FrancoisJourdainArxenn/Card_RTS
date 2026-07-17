using UnityEngine;
using TMPro;

public class UITooltip : MonoBehaviour
{
    public static UITooltip Instance { get; private set; }
    [SerializeField] private RectTransform canvasRectTransform;

    private TextMeshProUGUI tooltipText;
    private RectTransform backgroundRectTransform;
    private RectTransform rectTransform;

    private System.Func<string> getTooltipTextFunc;

    private void Awake()
    {
        Instance = this;
        backgroundRectTransform = transform.Find("background_UITooltip").GetComponent<RectTransform>();
        tooltipText = transform.Find("Text_UITooltip").GetComponent<TextMeshProUGUI>();
        rectTransform = GetComponent<RectTransform>();

        HideToolTip();
    }

    private void SetText(string tooltipTextToSet)
    {
        tooltipText.SetText(tooltipTextToSet);
        tooltipText.ForceMeshUpdate();

        Vector2 textSize = tooltipText.GetRenderedValues(false);
        Vector2 padding = new Vector2(8f, 8f); // Add some padding around the text
        
        backgroundRectTransform.sizeDelta = textSize += padding;
    }

    private void Update()
    {
        Vector2 anchoredPosition = Input.mousePosition / canvasRectTransform.localScale.x;

        if (anchoredPosition.x + backgroundRectTransform.rect.width > canvasRectTransform.rect.width)
        {
            anchoredPosition.x = canvasRectTransform.rect.width - backgroundRectTransform.rect.width;
        }
        else if (anchoredPosition.x < 0)
        {
            anchoredPosition.x = 0;
        }

        if (anchoredPosition.y + backgroundRectTransform.rect.height > canvasRectTransform.rect.height)
        {
            anchoredPosition.y = canvasRectTransform.rect.height - backgroundRectTransform.rect.height;
        }
        else if (anchoredPosition.y < 0)
        {
            anchoredPosition.y = 0;
        }

        rectTransform.anchoredPosition = anchoredPosition;
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

    private void SHowTooltip(System.Func<string> getTooltipTextFunc)
    {
        this.getTooltipTextFunc = getTooltipTextFunc;
        gameObject.SetActive(true);
        SetText(getTooltipTextFunc());
    }

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
