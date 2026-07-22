using UnityEngine;
using UnityEngine.EventSystems;

public class UITooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea(2,3)]
    [SerializeField] private string tooltipText;

    private System.Func<string> dynamicTextProvider;

    public void SetDynamicText(System.Func<string> provider)
    {
        dynamicTextProvider = provider;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (dynamicTextProvider != null)
            UITooltip.ShowTooltip_Static(dynamicTextProvider);
        else
            UITooltip.ShowTooltip_Static(tooltipText);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        UITooltip.HideTooltip_Static();
    }
}
