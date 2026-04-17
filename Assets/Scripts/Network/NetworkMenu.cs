using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class NetworkMenu : MonoBehaviour
{
    [Header("Boutons")]
    public Button hostButton;
    public Button clientButton;

    [Header("Texte de statut")]
    public TMP_Text statusText;

    void Start()
    {
        // On s'abonne aux événements de connexion pour afficher le statut
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // Appelé quand on clique sur "Héberger"
    public void StartHost()
    {
        statusText.text = "Démarrage du serveur...";
        NetworkManager.Singleton.StartHost();
    }

    // Appelé quand on clique sur "Rejoindre"
    public void StartClient()
    {
        statusText.text = "Connexion en cours...";
        NetworkManager.Singleton.StartClient();
    }

    // --- Événements de connexion ---

    void OnServerStarted()
    {
        statusText.text = "Serveur démarré. En attente d'un joueur...";
    }

    void OnClientConnected(ulong clientId)
    {
        statusText.text = "Joueur connecté ! (ID: " + clientId + ")";
        // Quand deux joueurs sont connectés, on cache le menu
        // Seul le serveur peut déclencher le chargement de scène.
        // Le client reçoit OnClientConnected uniquement pour lui-même,
        // et ne doit pas appeler LoadScene (ServerOnly).
        if (NetworkManager.Singleton.IsServer && NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            NetworkSessionData.LocalClientId = NetworkManager.Singleton.LocalClientId;
            NetworkSessionData.IsNetworkSession = true;
            NetworkManager.Singleton.SceneManager.LoadScene("BattleScene", UnityEngine.SceneManagement.LoadSceneMode.Single); // Charger la scène de jeu
        }
    }

    void OnClientDisconnected(ulong clientId)
    {
        statusText.text = "Déconnecté.";
    }

    void OnDestroy()
    {
        // Bonne pratique : se désabonner des événements quand l'objet est détruit
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}