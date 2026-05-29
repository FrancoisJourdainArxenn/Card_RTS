Deck.cs

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Deck : MonoBehaviour {

    public DeckSO playerDeck;

    public WeightedDrawConfig drawConfig;

    public Dictionary<CardAsset, int> timesDrawn = new Dictionary<CardAsset, int>();

    private int _n1, _n2, _n3;

    void Awake()
    {
        timesDrawn = new Dictionary<CardAsset, int>();
        CacheTierCounts();
    }

    public void LoadPreset(DeckSO preset)
    {
        if (preset == null) return;
        playerDeck = preset;
        timesDrawn.Clear();
        CacheTierCounts();
    }

    private void CacheTierCounts()
    {
        _n1 = 0; _n2 = 0; _n3 = 0;
        foreach (CardAsset card in playerDeck.cards)
        {
            int t = (int)card.tier;
            if (t == 1)      _n1++;
            else if (t == 2) _n2++;
            else if (t == 3) _n3++;
        }
    }

    public CardAsset DrawWeightedCard(int seed, int mainIncome, string playerTag = "?")
    {
        if (drawConfig == null)
            return null;

        CardAsset drawn = WeightedDraw.Draw(
            playerDeck.cards, timesDrawn, _n1, _n2, _n3,
            mainIncome, seed, drawConfig, playerTag);

        if (drawn != null)
            timesDrawn[drawn] = (timesDrawn.TryGetValue(drawn, out int v) ? v : 0) + 1;

        return drawn;
    }

    public void ResetTimesDrawn() => timesDrawn.Clear();

    public void ShuffleWithSeed(int seed)
    {
        playerDeck.cards.ShuffleWithSeed(seed);
    }

    public void SelectRandomCardFromSeed(int seed)
    {
        playerDeck.cards.SelectRandomCardFromSeed(seed);
    }

    public CardAsset FindBuildingByIndex(int index)
    {
        if (index < 0 || index >= playerDeck.buildings.Count) return null;
        return playerDeck.buildings[index];
    }
}
NetworkSessionData.cs

using UnityEngine;

/// <summary>
/// Données réseau persistantes entre les scènes.
/// Rempli dans NetworkMenu avant le chargement de la BattleScene.
/// </summary>
public static class NetworkSessionData
{
    /// <summary>ClientId Netcode du joueur local (0 = host, 1 = client).</summary>
    public static ulong LocalClientId { get; set; }

    /// <summary>Vrai si une session réseau est active (par opposition au jeu local).</summary>
    public static bool IsNetworkSession { get; set; }

    /// <summary>Prefab de la map sélectionnée pour la partie, assigné dans NetworkMenu.</summary>
    public static int SelectedMapIndex { get; set; } = 0;

    /// <summary>
    /// Index dans MenuRegistry.decks du preset sélectionné.
    /// -1 = utilise le playerDeck assigné dans la scène (défaut).
    /// </summary>
    public static int SelectedDeckPresetIndex { get; set; } = -1;

    /// <summary>Preset sélectionné pour le jeu local (non réseau). Null = défaut de scène.</summary>
    public static DeckSO SelectedDeckPreset { get; set; } = null;
}
MapLoader.cs

using UnityEngine;

[DefaultExecutionOrder(-100)]
public class MapLoader : MonoBehaviour
{
    public static Transform EnvironnementTransform { get; private set; }
    public static MapLoader Instance { get; private set; }

    [SerializeField] GameObject defaultMapPrefab;
    [SerializeField] MenuRegistry registry;

    void Awake()
    {
        Instance = this;
        EnvironnementTransform = transform;
        if (!NetworkSessionData.IsNetworkSession)
            Instantiate(defaultMapPrefab, transform.position, transform.rotation, transform);
    }

    public GameObject GetMapPrefab(int index) => registry.maps[index];
}
NetworkMenu.cs

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

    [Header("Content")]
    [SerializeField] MenuRegistry registry;

    [Header("Map")]
    [SerializeField] TMP_Dropdown mapDropdown;

    [Header("Deck")]
    [SerializeField] TMP_Dropdown deckDropdown;

    [Header("Scene")]
    [SerializeField] string battleSceneName = "BattleScene";

    private const ushort Port = 7777;

    void Start()
    {
        NetworkManager.Singleton.OnServerStarted            += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        var mapOptions = new System.Collections.Generic.List<TMP_Dropdown.OptionData> { new("Aléatoire") };
        foreach (var map in registry.maps)
            mapOptions.Add(new(map.name));
        mapDropdown.ClearOptions();
        mapDropdown.AddOptions(mapOptions);

        if (deckDropdown != null)
        {
            var deckOptions = new System.Collections.Generic.List<TMP_Dropdown.OptionData> { new("Défaut") };
            foreach (var deck in registry.decks)
                deckOptions.Add(new(deck.deckName));
            deckDropdown.ClearOptions();
            deckDropdown.AddOptions(deckOptions);
        }
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

        NetworkSessionData.SelectedDeckPresetIndex = (deckDropdown != null) ? deckDropdown.value - 1 : -1;

        NetworkManager.Singleton.StartClient();
        mapDropdown.gameObject.SetActive(false);
        if (deckDropdown != null) deckDropdown.gameObject.SetActive(false);
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
                ? Random.Range(0, registry.maps.Length)
                : idx - 1;

            int deckIdx = (deckDropdown != null) ? deckDropdown.value - 1 : -1;
            NetworkSessionData.SelectedDeckPresetIndex = deckIdx;
            NetworkSessionData.SelectedDeckPreset = (deckIdx >= 0 && deckIdx < registry.decks.Length)
                ? registry.decks[deckIdx]
                : null;

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
TurnManager.cs
Seulement OnGameStart change — voici la méthode complète à remplacer (ligne 53 à 120) :


public void OnGameStart(int? seed = null, int[] cardInHandIDs = null, int deckIdxLow = -1, int deckIdxTop = -1)
{
    EffectRegistry.Reset();
    if (Player.Players == null || Player.Players.Length < 2)
    {
        // Debug.LogError("TurnManager: need at least 2 Player instances.");
        return;
    }

    foreach (Player p in Player.Players)
    {
        p.LoadCharacterInfoFromAsset();
        p.TransmitInfoAboutPlayerToVisual();
    }

    if (NetworkSessionData.IsNetworkSession)
    {
        foreach (Player p in Player.Players)
        {
            bool isLow = p == GlobalSettings.Instance.LowPlayer;
            DeckSO preset = GameNetworkManager.Instance.GetDeckPresetForPlayer(isLow ? deckIdxLow : deckIdxTop);
            if (preset != null) p.deck.LoadPreset(preset);
        }
    }
    else if (NetworkSessionData.SelectedDeckPreset != null)
    {
        foreach (Player p in Player.Players)
            p.deck.LoadPreset(NetworkSessionData.SelectedDeckPreset);
    }

    if (seed.HasValue)
    {
        for (int idx = 0; idx < Player.Players.Length; idx++)
        {
            Player p = Player.Players[idx];
            p.deck.playerDeck.cards.ShuffleWithSeed(seed.Value + idx);
            p.deck.ResetTimesDrawn();
        }
    }
    else
    {
        for (int idx = 0; idx < Player.Players.Length; idx++)
        {
            Player p = Player.Players[idx];
            p.deck.playerDeck.cards.Shuffle();
            p.deck.ResetTimesDrawn();
        }
    }

    CardLogic.CardsCreatedThisGame.Clear();
    CreatureLogic.CreaturesCreatedThisGame.Clear();
    BuildingLogic.BuildingsCreatedThisGame.Clear();

    EnsurePhaseReadyMatchesPlayers();
    ResetPhaseReadyFlags();

    int drawSeedOffset = 0;
    int deckSeed = seed ?? 0;
    for (int i = 0; i < initdraw; i++)
    {
        for (int j = 0; j < Player.Players.Length; j++)
        {
            Player p = Player.Players[j];
            int cardInHandID = cardInHandIDs == null ? -1 : cardInHandIDs[j * initdraw + i];
            p.DrawACard(true, cardInHandID, deckSeed + drawSeedOffset++);
        }
    }

    if (NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer)
        GameNetworkManager.Instance.InitDrawSeedOffset(drawSeedOffset);
    foreach (Player p in Player.Players)
        p.OnTurnStart();

    EnterPhase(TurnPhases.Command);
    StartCoroutine(HighlightAfterDraws());
}
GameNetworkManager.cs — 3 sections à modifier
Section 1 — champs après Instance (remplace les lignes 15-22) :


public static GameNetworkManager Instance { get; private set; }
NetworkVariable<int> mapIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
[SerializeField] MenuRegistry registry;
private readonly Dictionary<ulong, int> _deckChoices = new();

// Compteur côté serveur : combien de clients ont signalé qu'ils sont prêts
private int readyCount = 0;
private Dictionary<TurnManager.TurnPhases, HashSet<int>> _pendingEndPhase = new();
Section 2 — OnNetworkSpawn + GetDeckPresetForPlayer (remplace les lignes 413-432) :


public override void OnNetworkSpawn()
{
    if (IsServer)
        mapIndex.Value = NetworkSessionData.SelectedMapIndex;

    LoadMap(mapIndex.Value);

    NetworkSessionData.LocalClientId = NetworkManager.Singleton.LocalClientId;
    PlayerReadyServerRpc(NetworkManager.Singleton.LocalClientId, NetworkSessionData.SelectedDeckPresetIndex);
}

public DeckSO GetDeckPresetForPlayer(int idx)
{
    if (idx < 0 || registry == null || idx >= registry.decks.Length) return null;
    return registry.decks[idx];
}

void LoadMap(int index)
{
    Transform env = MapLoader.EnvironnementTransform;
    if (env == null) { Debug.LogError("EnvironnementTransform est null"); return; }
    Instantiate(MapLoader.Instance.GetMapPrefab(index), env.position, env.rotation, env);
    if (GlobalSettings.Instance != null) GlobalSettings.Instance.InitFromMap();
    if (FogMapOverlay.Instance != null) FogMapOverlay.Instance.ComputeMapBounds();
}
Section 3 — PlayerReadyServerRpc + StartGameClientRpc (remplace les lignes 438-471) :


[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
void PlayerReadyServerRpc(ulong clientId = 0, int deckIndex = -1)
{
    _deckChoices[clientId] = deckIndex;
    readyCount++;
    Debug.Log($"[GameNetworkManager] Joueur prêt : {readyCount}/2");

    if (readyCount >= 2)
    {
        int deckLow = _deckChoices.TryGetValue(0, out int dLow) ? dLow : -1;
        int deckTop = _deckChoices.TryGetValue(1, out int dTop) ? dTop : -1;
        deckSeed.Value = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        int[] cardInHandIDs = new int[TurnManager.Instance.initdraw * Player.Players.Length];
        for (int i = 0; i < cardInHandIDs.Length; i++)
            cardInHandIDs[i] = IDFactory.GetUniqueID();
        Debug.Log("[GameNetworkManager] Les deux joueurs sont prêts. Démarrage de la partie.");
        StartGameClientRpc(deckSeed.Value, cardInHandIDs, deckLow, deckTop);
    }
}

/// <summary>Envoyé par le serveur à TOUS les clients pour démarrer la partie.</summary>
[ClientRpc]
void StartGameClientRpc(int deckSeed, int[] cardInHandIDs, int deckIdxLow = -1, int deckIdxTop = -1)
{
    AssignLocalPlayerControl();
    TurnManager.Instance.OnGameStart(deckSeed, cardInHandIDs, deckIdxLow, deckIdxTop);
    GlobalSettings.Instance.RefreshEndPhaseButtons();
}
Dans Unity : glisse ton asset MenuRegistry dans le champ Registry sur NetworkMenu (MenuScene), MapLoader (BattleScene) et GameNetworkManager (BattleScene).