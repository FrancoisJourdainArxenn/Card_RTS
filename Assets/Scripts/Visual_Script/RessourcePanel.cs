using UnityEngine;
using TMPro;

public class RessourcePanel : MonoBehaviour
{
    public int mainRessourceTotal;
    public int mainRessourceAvailable;
    public int secondRessourceTotal;
    public int secondRessourceAvailable;
    public TMP_Text mainRessourceText;
    public TMP_Text secondRessourceText;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        mainRessourceText.text = mainRessourceAvailable.ToString();
        secondRessourceText.text = secondRessourceAvailable.ToString();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
