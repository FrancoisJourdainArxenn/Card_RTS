using UnityEngine;
using UnityEngine.EventSystems;

public class UITooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea(2,3)]
    [SerializeField] private string tooltipText;

    private string prefix;
    private System.Func<string> dynamicTextProvider;

    public void SetDynamicText(System.Func<string> provider)
    {
        dynamicTextProvider = provider;
    }

    // Stocke le prefixe a part (au lieu de le fusionner directement dans tooltipText) pour pouvoir
    // etre appele plusieurs fois (ex: apres un changement de cardPool) sans empiler les anciens prefixes.
    public void PrependText(string newPrefix)
    {
        prefix = newPrefix;
    }

    private string BuildTooltipText()
    {
        if (string.IsNullOrEmpty(prefix))
            return tooltipText;
        return string.IsNullOrEmpty(tooltipText) ? prefix : $"{prefix}\n{tooltipText}";
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (dynamicTextProvider != null)
            UITooltip.ShowTooltip_Static(dynamicTextProvider);
        else
            UITooltip.ShowTooltip_Static(BuildTooltipText());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        UITooltip.HideTooltip_Static();
    }
}
