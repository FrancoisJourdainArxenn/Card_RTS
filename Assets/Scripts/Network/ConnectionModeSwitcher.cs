using UnityEngine;
using UnityEngine.UI;

public class ConnectionModeSwitcher : MonoBehaviour
{
    [Header("Panneaux")]
    [SerializeField] public GameObject multiplayerPanel;
    [SerializeField] public GameObject lanPanel;
    [SerializeField] public GameObject soloPanel;

    [Header("Boutons d'onglet")]
    [SerializeField] public Button multiplayerTabButton;
    [SerializeField] public Button lanTabButton;
    [SerializeField] public Button soloTabButton;

    void Start()
    {
        ShowMultiplayer();
    }

    public void ShowMultiplayer()
    {
        multiplayerPanel.SetActive(true);
        lanPanel.SetActive(false);
        soloPanel.SetActive(false);

        multiplayerTabButton.interactable = false; // grisé = onglet actif
        lanTabButton.interactable = true;
        soloTabButton.interactable = true;
    }

    public void ShowLAN()
    {
        lanPanel.SetActive(true);
        multiplayerPanel.SetActive(false);
        soloPanel.SetActive(false);

        lanTabButton.interactable = false;
        multiplayerTabButton.interactable = true;
        soloTabButton.interactable = true;
    }

    public void ShowSolo()
    {
        soloPanel.SetActive(true);
        multiplayerPanel.SetActive(false);
        lanPanel.SetActive(false);

        soloTabButton.interactable = false;
        multiplayerTabButton.interactable = true;
        lanTabButton.interactable = true;
    }
}
