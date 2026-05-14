using UnityEngine;
using UnityEngine.UI;

public class ConfirmButtonFeedback : MonoBehaviour
{
    [ColorUsage(true, true)] public Color normalColor = Color.white;
    [ColorUsage(true, true)] public Color readyColor  = Color.white;
    public GameObject glowObject;

    public void SetReadyState(bool ready)
    {
        GetComponent<Image>().color = ready ? readyColor : normalColor;
        if (glowObject != null)
            glowObject.SetActive(ready);
    }
}
