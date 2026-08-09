using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
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
    public Color statBuffColor   = Color.blue;
    public Color statDebuffColor = Color.red;

    [Header("Neutral Base")]
    public NeutralZoneController[] NeutralBases;
    

    [Header("Numbers and Values")]
    public int initdraw = 4;
    public int MaxCreaturePerRow = 5;
    public float CardPreviewTime = 1f;
    public float CardTransitionTime = 1f;
    public float CardPreviewTimeFast = 0.2f;
    public float CardTransitionTimeFast = 0.5f;
    public float AttackMoveDuration = 0.3f;   // durée du mouvement aller-retour
    public float AttackPostDelay = 0.3f;       // pause après chaque attaque
    public float AttackWindupDuration = 0.15f; // durée de l'élan (recul + élévation) avant le coup
    public float AttackWindupBack = 0.3f;      // distance de recul pendant l'élan
    public float AttackWindupHeight = 0.35f;   // hauteur d'élévation pendant l'élan
    public float ProjectileSpeed = 18f;        // vitesse du projectile (unités/seconde) — la durée de vol dépend de la distance réelle à la cible
    public float ProjectileMinDuration = 0.12f; // durée de vol plancher, pour qu'un tir à courte distance ou un attaquant très rapide reste visible

    [Header("Pop Animation")]
    public float popStrength = 0.35f;
    public float popDuration = 0.35f;

    [Header("Camera Shake (on damage dealt)")]
    [Tooltip("How long (seconds) before the visual impact the camera shake starts, so it reads as landing on the hit instead of after it.")]
    public float CameraShakeAnticipation = 0.05f;
    [Tooltip("Attack stat threshold (X) at or above which a unit dealing damage triggers a light camera shake.")]
    public int CameraShakeThresholdLight = 5;
    public float CameraShakeStrengthLight = 0.15f;
    public float CameraShakeDurationLight = 0.2f;
    [Tooltip("Attack stat threshold (Y) at or above which a unit dealing damage triggers a strong camera shake.")]
    public int CameraShakeThresholdStrong = 9;
    public float CameraShakeStrengthStrong = 0.35f;
    public float CameraShakeDurationStrong = 0.3f;

    [Header("Prefabs and Assets")]
    public GameObject NoTargetSpellCardPrefab;
    public GameObject TargetedSpellCardPrefab;
    public GameObject CreatureCardPrefab;
    public GameObject HeroCardPrefab;
    public GameObject CreaturePrefab;
    public GameObject DamageEffectPrefab;
    public GameObject ExplosionPrefab;
    public GameObject RangedProjectilePrefab;
    public GameObject NeutralBasePrefab;
    public GameObject BuildingPrefab;

    [Header("Card Tier Icons")]
    public Sprite[] CardTierIcons = new Sprite[3]; // index 0 = T1, 1 = T2, 2 = T3

    [Header("Row Keywords")]
    public Keyword MeleeRowKeyword;
    public Keyword RangedRowKeyword;

    
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

        TopPlayer.PAreas = MapManager.Current.TopPlayerAreas;
        LowPlayer.PAreas = MapManager.Current.LowPlayerAreas;

        ZoneManager topZone = MapManager.Current.TopPlayerBaseSpawn.GetComponentInParent<ZoneManager>();
        if (topZone != null)
            TopPlayer.MainPArea = System.Array.Find(topZone.GetComponentsInChildren<PlayerArea>(), pa => pa.owner == AreaPosition.Top);

        ZoneManager lowZone = MapManager.Current.LowPlayerBaseSpawn.GetComponentInParent<ZoneManager>();
        if (lowZone != null)
            LowPlayer.MainPArea = System.Array.Find(lowZone.GetComponentsInChildren<PlayerArea>(), pa => pa.owner == AreaPosition.Low);


        SpawnMainBase(TopPlayer, MapManager.Current.TopPlayerBaseSpawn);
        SpawnMainBase(LowPlayer, MapManager.Current.LowPlayerBaseSpawn);
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
        bool confirmActive = localPlayer != null && PhaseEffectPipeline.IsTargetingActiveForPlayer(localPlayer);

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

        bool canConfirmTargeting = isLocalPlayer && hasActiveTargeting && !waitingForSelection;
        bool canEndPhaseNormally = isLocalPlayer && gameActive && notYetReady && !waitingForSelection;
        button.interactable = canConfirmTargeting || canEndPhaseNormally;
    }

}
