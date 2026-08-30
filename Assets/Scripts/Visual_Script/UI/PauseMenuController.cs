using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class PauseMenuController : MonoBehaviour
{
    [SerializeField] GameObject menuPanel;
    [SerializeField] RectTransform menuBackground;
    [SerializeField] KeyCode toggleKey = KeyCode.Escape;

    void Start()
    {
        menuPanel.SetActive(false);
        IsOpen = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Toggle();

        if (menuPanel.activeSelf && Input.GetMouseButtonDown(0))
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(menuBackground, Input.mousePosition))
                Close();
        }
    }

    public static bool IsOpen { get; private set; }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        menuPanel.SetActive(true);
        IsOpen = true;
    }

    public void Close()
    {
        menuPanel.SetActive(false);
        IsOpen = false;
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void Concede()
    {
        Close();

        Player localPlayer = GlobalSettings.Instance.localPlayer;

        if (NetworkSessionData.IsNetworkSession)
            GameNetworkManager.Instance.ConcedeServerRpc(localPlayer.playerIndex);
        else
            new GameOverCommand(localPlayer.otherPlayer.PlayerID, false).AddToQueue();
    }

    public void GoToMainMenu()
    {
        IDFactory.ResetIDs();
        IDHolder.ClearIDHoldersList();
        CardLogic.CardsCreatedThisGame.Clear();
        CreatureLogic.CreaturesCreatedThisGame.Clear();
        BuildingLogic.BuildingsCreatedThisGame.Clear();
        Command.CommandQueue.Clear();
        Command.CommandExecutionComplete();
        NetworkSessionData.IsNetworkSession = false;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        SceneManager.LoadScene("MenuScene");
    }
}
