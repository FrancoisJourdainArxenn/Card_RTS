using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class GlobalSettings : MonoBehaviour
{
    [Header("Players")]
    public Player TopPlayer;
    public Player LowPlayer;
    public Player localPlayer;
    
    [Tooltip("End phase button for the low-area human player.")]
    public Button EndTurnButton;
    public HandVisual localPlayerHand;
    public UiPlayerVisual UiPlayerVisual;
    public BuildingShopVisual buildingShop;

    [Header("Colors")]
    public Color32 TopColor;
    public Color32 LowColor;
    public Color32 NeutralColor;

    [Header("Neutral Base")]
    public NeutralZoneController[] NeutralBases;


    [Header("Numbers and Values")]
    public int initdraw = 4;
    public int MaxCreaturePerRow = 5;

    [Header("Prefabs and Assets")]
    public GameObject NoTargetSpellCardPrefab;
    public GameObject TargetedSpellCardPrefab;
    public GameObject CreatureCardPrefab;
    public GameObject HeroCardPrefab;
    public GameObject CreaturePrefab;
    public GameObject DamageEffectPrefab;
    public GameObject ExplosionPrefab;
    public GameObject RangedProjectilePrefab;
    public GameObject MeleeMultiTargetVfxPrefab;
    public GameObject NeutralBasePrefab;
    public GameObject BuildingPrefab;

    [Header("Row Keywords")]
    public Keyword MeleeRowKeyword;
    public Keyword RangedRowKeyword;
    public Sprite MeleeIcon;
    public Sprite RangedIcon;

    
    [Header("Other")]
    public Button ConfirmTargetingButton;
    public GameObject baseApparitionPoint;
    public GameObject GameOverPanel;
    public TMP_Text localPlayerDebugText;

    [Header("Debug")]
    public bool UseDeferredMovesInSolo = false;

    public Dictionary<AreaPosition, Player> Players = new Dictionary<AreaPosition, Player>();

    public static GlobalSettings Instance;

    void Awake()
    {
        Players.Add(AreaPosition.Top, TopPlayer);
        Players.Add(AreaPosition.Low, LowPlayer);
        TopPlayer.playerColor = TopColor;
        LowPlayer.playerColor = LowColor;
        Instance = this;
        InitFromMap();
    }
    void Start()
    {
        LowPlayer.tag = "LowPlayer";
        TopPlayer.tag = "TopPlayer";

        if (ConfirmTargetingButton != null)
            ConfirmTargetingButton.gameObject.SetActive(false);


        if (NetworkSessionData.IsNetworkSession == false)
        {
            localPlayer = LowPlayer;
            localPlayerDebugText.text = "Local Player: " + localPlayer.name;    
        }
        UiPlayerVisual?.RefreshUI();
        FogOfWarManager.Refresh();
        BuildSpotVisual.RefreshAll();
    }

    public void InitFromMap()
    {
        if (MapManager.Current == null)
        {
            //Debug.LogError("Aucun MapManager dans la scène !");
            return;
        }

        NeutralBases = MapManager.Current.NeutralBases;
        foreach (NeutralZoneController n in NeutralBases)
        {
            n.owner = AreaPosition.Neutral;
            n.SetOwnerColor(NeutralColor);
        }

        // FindObjectsByType(..., FindObjectsSortMode.None) n'est pas stable entre host et client
        // (même remarque que Player.Awake()) — on trie par chemin de hiérarchie pour garantir un
        // ordre identique partout, puisque Player.InitBaseIDs() attribue les baseID par position
        // dans ce tableau : un ordre différent entre machines attribuerait un baseID différent à
        // la même PlayerArea physique, et tout ce qui s'échange des baseID par réseau désyncerait.
        PlayerArea[] allAreas = FindObjectsByType<PlayerArea>(FindObjectsSortMode.None);
        System.Array.Sort(allAreas, (a, b) =>
            string.CompareOrdinal(GetHierarchyPath(a.transform), GetHierarchyPath(b.transform)));
        TopPlayer.PAreas = System.Array.FindAll(allAreas, a => a.owner == AreaPosition.Top);
        LowPlayer.PAreas = System.Array.FindAll(allAreas, a => a.owner == AreaPosition.Low);

        ZoneManager topZone = MapManager.Current.TopPlayerBaseSpawn.GetComponentInParent<ZoneManager>();
        if (topZone != null)
            TopPlayer.MainPArea = System.Array.Find(topZone.GetComponentsInChildren<PlayerArea>(), pa => pa.owner == AreaPosition.Top);

        ZoneManager lowZone = MapManager.Current.LowPlayerBaseSpawn.GetComponentInParent<ZoneManager>();
        if (lowZone != null)
            LowPlayer.MainPArea = System.Array.Find(lowZone.GetComponentsInChildren<PlayerArea>(), pa => pa.owner == AreaPosition.Low);


        SpawnMainBase(TopPlayer, MapManager.Current.TopPlayerBaseSpawn);
        SpawnMainBase(LowPlayer, MapManager.Current.LowPlayerBaseSpawn);
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    void SpawnMainBase(Player player, Transform spawnPoint)
    {
        if (player.baseVisual != null) return;

        GameObject go = Instantiate(MapManager.Current.MainBasePrefab, spawnPoint.position, spawnPoint.rotation);
        go.tag = player.tag;
        MainBaseVisual visual = go.GetComponent<MainBaseVisual>();
        visual.player = player;
        player.baseVisual = visual;

        if (visual.baseManager != null)
            visual.baseManager.ResetValues(player.baseAsset);

        IDHolder id = go.GetComponent<IDHolder>() ?? go.AddComponent<IDHolder>();
        id.UniqueID = player.PlayerID;

        player.CalculatePlayerIncome();
    }


    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            localPlayer = localPlayer == TopPlayer ? LowPlayer : TopPlayer;
            localPlayerDebugText.text = "Local Player: " + localPlayer.name;
            FogOfWarManager.Refresh();
            PathVisual.RefreshAll();
            RefreshEndPhaseButtons();
            BuildSpotVisual.RefreshAll();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (OnPlayTargetingSession.IsActive)
                OnPlayTargetingSession.Cancel();
            else
                Draggable.CancelCurrentDrag();
        }

        if (OnPlayTargetingSession.IsActive && Input.GetMouseButtonDown(0))
            CancelOnPlayTargetingIfClickMissed();

        // Filet de sécurité pour le clic droit : contrairement au clic gauche (qui doit distinguer
        // "raté" d'un vrai choix de cible), le clic droit annule TOUJOURS pendant une session de
        // ciblage — y compris au-dessus du vide, où ni HoverPreview ni ZoneManager ne reçoivent de
        // clic pour intercepter le cas eux-mêmes. Redondant (donc sans effet) si l'annulation a déjà
        // eu lieu via l'un de ces composants dans la même frame, Cancel() étant un no-op hors session active.
        if (OnPlayTargetingSession.IsActive && Input.GetMouseButtonDown(1))
            OnPlayTargetingSession.Cancel();
    }

    // Clic dans le vide (aucune entité 3D ni élément UI sous le curseur) pendant une session de
    // ciblage OnPlay : comme un drag relâché dans une zone non valide, on annule et la carte
    // retourne en main. Les clics sur une entité/zone existante (valide ou non) sont déjà gérés
    // par OnPlayTargetingSession.OnEntityClicked (voir PhaseEffectPipeline.OnEntityClicked).
    private void CancelOnPlayTargetingIfClickMissed()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.GetComponentInParent<IDHolder>() != null)
                return;
        }

        OnPlayTargetingSession.Cancel();
    }
    
    public bool CanControlThisPlayer(AreaPosition owner)
    {
        return CanControlThisPlayer(Players[owner]);
    }

    public bool CanControlThisPlayer(Player ownerPlayer)
    {
        if (ownerPlayer == null || TurnManager.Instance == null)
            return false;
        bool NotDrawingAnyCards = !Command.CardDrawPending();
        return ownerPlayer.MainPArea.AllowedToControlThisPlayer
            && ownerPlayer.MainPArea.ControlsON
            && TurnManager.Instance.MayPlayerUseControlsInPhase(ownerPlayer)
            && NotDrawingAnyCards;
    }

    public void RefreshEndPhaseButtons()
    {
        // Bouton de validation du targeting supprimé : la sélection manuelle de la cible
        // suffit désormais (voir PhaseEffectPipeline.OnEntityClicked), donc on ne bascule
        // plus jamais vers ConfirmTargetingButton — seul le bouton End Phase normal reste,
        // grisé tant que des cibles restent à choisir.
        bool confirmActive = false;

        foreach (EndTurnButton eb in Object.FindObjectsByType<EndTurnButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Button btn = eb.GetComponent<Button>();
            if (btn == null)
                continue;
            Player player = eb.GetParticipantPlayer();

            if (player == localPlayer)
                eb.gameObject.SetActive(!confirmActive);

            SetEndPhaseButtonState(btn, player);
        }

        if (ConfirmTargetingButton != null)
        {
            ConfirmTargetingButton.gameObject.SetActive(confirmActive);
            ConfirmTargetingButton.interactable = true;
            bool allAssigned = confirmActive && PhaseEffectPipeline.AreAllTargetsAssigned(localPlayer);
            ConfirmTargetingButton.GetComponent<ConfirmButtonFeedback>()?.SetReadyState(allAssigned);
        }
    }

    static void SetEndPhaseButtonState(Button button, Player player)
    {
        if (button == null || player == null)
            return;

        bool isLocalPlayer        = player.MainPArea.AllowedToControlThisPlayer;
        bool gameActive           = player.MainPArea.ControlsON;
        bool notYetReady          = !TurnManager.Instance.HasPlayerRegisteredEndPhase(player);
        bool hasActiveTargeting   = PhaseEffectPipeline.IsTargetingActiveForPlayer(player);
        bool waitingForSelection  = hasActiveTargeting && PhaseEffectPipeline.BlocksEndPhaseButton(player);
        bool waitingForCardChoice = ChooseOneManager.Instance != null && ChooseOneManager.Instance.HasPendingChoice(player);

        bool canConfirmTargeting = isLocalPlayer && hasActiveTargeting && !waitingForSelection;
        bool canEndPhaseNormally = isLocalPlayer && gameActive && notYetReady && !waitingForSelection && !waitingForCardChoice;
        button.interactable = canConfirmTargeting || canEndPhaseNormally;
    }

}
