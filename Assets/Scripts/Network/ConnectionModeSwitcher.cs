using UnityEngine;
using UnityEngine.UI;

public class ConnectionModeSwitcher : MonoBehaviour
{
    [Header("Panneaux")]
    [SerializeField] public GameObject multiplayerPanel;
    [SerializeField] public GameObject lanPanel;

    [Header("Boutons d'onglet")]
    [SerializeField] public Button multiplayerTabButton;
    [SerializeField] public Button lanTabButton;

    void Start()
    {
        ShowMultiplayer();
    }

    public void ShowMultiplayer()
    {
        multiplayerPanel.SetActive(true);
        lanPanel.SetActive(false);

        multiplayerTabButton.interactable = false; // grisé = onglet actif
        lanTabButton.interactable = true;
    }

    public void ShowLAN()
    {
        lanPanel.SetActive(true);
        multiplayerPanel.SetActive(false);

        lanTabButton.interactable = false;
        multiplayerTabButton.interactable = true;
    }
}
