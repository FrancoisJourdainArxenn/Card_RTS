using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.UI;
using TMPro;

public class NetworkMenu : MonoBehaviour
{
    [Header("Boutons")]
    public Button hostButton;
    public Button clientButton;

    [Header("UI")]
    public TMP_InputField ipInputField;
    public TMP_Text statusText;

    [Header("Map")]
    [SerializeField] TMP_Dropdown mapDropdown;
    [SerializeField] GameObject[] mapPrefabs;



    private const ushort Port = 7777;

    void Start()
    {
        NetworkManager.Singleton.OnServerStarted             += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback   += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback  += OnClientDisconnected;
    
        var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData> { new("Aléatoire") };
        foreach (var prefab in mapPrefabs)
            options.Add(new(prefab.name));
        mapDropdown.ClearOptions();
        mapDropdown.AddOptions(options);

    }

    public void StartHost()
    {
        statusText.text = "Démarrage du serveur...";
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData("0.0.0.0", Port);
        NetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        string ip = ipInputField.text.Trim();
        statusText.text = $"Connexion vers {ip}...";
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip, Port);
        NetworkManager.Singleton.StartClient();
        mapDropdown.gameObject.SetActive(false);

    }

    void OnServerStarted() =>
        statusText.text = "Serveur démarré. En attente d'un joueur...";

    void OnClientConnected(ulong clientId)
    {
        statusText.text = $"Joueur connecté ! (ID: {clientId})";
        if (NetworkManager.Singleton.IsServer && NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            NetworkSessionData.LocalClientId = NetworkManager.Singleton.LocalClientId;
            NetworkSessionData.IsNetworkSession = true;
            
            int idx = mapDropdown.value;
            NetworkSessionData.SelectedMapIndex = idx == 0
                ? Random.Range(0, mapPrefabs.Length)
                : idx - 1;

            NetworkManager.Singleton.SceneManager.LoadScene("BattleScene",
                UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    void OnClientDisconnected(ulong clientId) =>
        statusText.text = "Déconnecté.";

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted            -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}
