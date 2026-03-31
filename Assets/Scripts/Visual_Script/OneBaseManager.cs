using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class OneBaseManager : MonoBehaviour
{
    public BaseAsset baseAsset;

    [Header("Text Component References")]

    public TMP_Text MainRessourceIncome;
    public TMP_Text SecondRessourceIncome;
    public TMP_Text HealthText;

    [Header("Image References")]
    //public Image CardGraphicImage;
    public Image ArtImage;
    public Image FrameImage;
    public Image CardFaceGlowImage;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (baseAsset != null)
            ReadBaseFromAsset();
    }

    public void ReadBaseFromAsset()
    {

        HealthText.text = baseAsset.MaxHealth.ToString();
        MainRessourceIncome.text = baseAsset.mainRessourceIncome.ToString();
        SecondRessourceIncome.text = baseAsset.secondRessourceIncome.ToString();
        ArtImage.sprite = baseAsset.BaseImage;
    }

    public void ResetValues(BaseAsset baseAsset)
    {
        ReadBaseFromAsset();
    }
}
