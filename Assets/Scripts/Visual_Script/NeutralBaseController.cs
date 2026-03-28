using UnityEngine;
using UnityEngine.UI;

public class NeutralBaseController : MonoBehaviour
{
    public AreaPosition owner;
    public Color ownerColor;
    public GameObject background;

    public void SetOwnerColor(Color color)
    {
        ownerColor = color;
        background.GetComponent<Image>().color = ownerColor;
    }
}
