using System.Collections;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class NetworkMenu : MonoBehaviour
{
    [Header("Boutons")]
    public Button hostButton;
    public Button clientButton;

    [Header("UI")]
    public TMP_InputField ipInputField;
    public TMP_Text statusText;

    [Header("Multiplayer (Relay)")]
    public TMP_InputField relayJoinCodeInputField;
    public TMP_Text relayCodeDisplayText;

    [Header("Selectable")]
    [SerializeField] MenuRegistry menuRegistry;
    [SerializeField] TMP_Dropdown mapDropdown;


    [Header("Scene")]
    [SerializeField] string battleSceneName = "BattleScene";



    private const ushort Port = 7777;

    void Start()
    {
        NetworkManager.Singleton.OnServerStarted             += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback   += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback  += OnClientDisconnected;
    
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData> { new("Aléatoire") };
        foreach (GameObject prefab in menuRegistry.maps)
            options.Add(new(prefab.name));
        mapDropdown.ClearOptions();
        mapDropdown.AddOptions(options);
    }

    private int GetSelectedDeckPresetIndex()
    {
        DeckSO selected = HeroPortrait.SelectedDeck;
        if (selected == null)
            return -1;

        return System.Array.IndexOf(menuRegistry.decks, selected);
    }

    public void StartHost()
    {
        statusText.text = "Démarrage du serveur...";
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData("0.0.0.0", Port);
        NetworkManager.Singleton.StartHost();
    }

    private Coroutine _connectRoutine;

    public void StartClient()
    {
        if (_connectRoutine != null)
            StopCoroutine(_connectRoutine);

        string ip = ipInputField.text.Trim();
        _connectRoutine = StartCoroutine(ConnectClientRoutine(ip));
    }

    private IEnumerator ConnectClientRoutine(string ip)
    {
        // Si une tentative précédente (mauvaise IP, timeout en cours...) tourne encore,
        // NetworkManager refuse silencieusement un nouveau StartClient() tant qu'elle
        // n'est pas totalement arrêtée. On force donc l'arrêt et on attend la fin réelle
        // du shutdown (différé de quelques frames en interne) avant de relancer.
        if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
        {
            statusText.text = "Annulation de la tentative précédente...";
            NetworkManager.Singleton.Shutdown();
            yield return new WaitUntil(() =>
                !NetworkManager.Singleton.ShutdownInProgress && !NetworkManager.Singleton.IsListening);
        }

        statusText.text = $"Connexion vers {ip}...";
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip, Port);
        NetworkSessionData.SelectedDeckPresetIndex = GetSelectedDeckPresetIndex();

        NetworkManager.Singleton.StartClient();
        mapDropdown.gameObject.SetActive(false);

        _connectRoutine = null;
    }

    public async void StartHostRelay()
    {
        statusText.text = "Connexion aux services Unity...";
        await UgsBootstrap.EnsureReadyAsync();

        statusText.text = "Création de la session distante...";
        NetworkSessionData.SelectedDeckPresetIndex = GetSelectedDeckPresetIndex();

        SessionOptions options = new SessionOptions
        {
            MaxPlayers = 2,
            Name = "CardRTS-Session"
        }.WithRelayNetwork();

        try
        {
            IHostSession session = await MultiplayerService.Instance.CreateSessionAsync(options);
            statusText.text = $"Session créée ! Code à partager : {session.Code}";
            if (relayCodeDisplayText != null)
                relayCodeDisplayText.text = session.Code;
        }
        catch (SessionException e)
        {
            statusText.text = $"Erreur : {e.Message}";
        }
    }

    public async void JoinRelay()
    {
        string joinCode = relayJoinCodeInputField.text.Trim().ToUpperInvariant();

        statusText.text = "Connexion aux services Unity...";
        await UgsBootstrap.EnsureReadyAsync();

        statusText.text = $"Connexion à {joinCode}...";
        NetworkSessionData.SelectedDeckPresetIndex = GetSelectedDeckPresetIndex();

        try
        {
            await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);
            mapDropdown.gameObject.SetActive(false);
        }
        catch (SessionException e)
        {
            statusText.text = $"Erreur : {e.Message}";
        }
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
                ? Random.Range(0, menuRegistry.maps.Length)
                : idx - 1;
            
            NetworkSessionData.SelectedDeckPresetIndex = GetSelectedDeckPresetIndex();
            NetworkManager.Singleton.SceneManager.LoadScene(battleSceneName,
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
