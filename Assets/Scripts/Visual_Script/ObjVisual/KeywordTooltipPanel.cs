using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KeywordTooltipPanel : MonoBehaviour
{
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public Image KeywordIcon;

    public void Setup(Keyword keyword)
    {
        titleText.text = keyword.displayName;
        descriptionText.text = keyword.description;

        bool hasIcon = keyword.icon != null;
        KeywordIcon.gameObject.SetActive(hasIcon);
        if (hasIcon)
            KeywordIcon.sprite = keyword.icon;
    }
}
