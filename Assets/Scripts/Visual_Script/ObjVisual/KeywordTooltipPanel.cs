using TMPro;
using UnityEngine;

public class KeywordTooltipPanel : MonoBehaviour
{
    public TMP_Text titleText;
    public TMP_Text descriptionText;

    public void Setup(Keyword keyword)
    {
        titleText.text = keyword.displayName;
        descriptionText.text = keyword.description;
    }
}
