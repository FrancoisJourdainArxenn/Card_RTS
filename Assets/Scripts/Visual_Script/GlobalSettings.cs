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
    public float CardPreviewTime = 1f;
    public float CardTransitionTime = 1f;
    public float CardPreviewTimeFast = 0.2f;
    public float CardTransitionTimeFast = 0.5f;
    [Header("Prefabs and Assets")]
    public GameObject NoTargetSpellCardPrefab;
    public GameObject TargetedSpellCardPrefab;
    public GameObject CreatureCardPrefab;
    public GameObject CreaturePrefab;
    public GameObject DamageEffectPrefab;
    public GameObject ExplosionPrefab;
    public GameObject NeutralBasePrefab;
    public GameObject BuildingPrefab;
    [Header("Other")]
    public Button ConfirmTargetingButton;

    public GameObject GameOverPanel;
    public TMP_Text localPlayerDebugText;

    public Dictionary<AreaPosition, Player> Players = new Dictionary<AreaPosition, Player>();

    public static GlobalSettings Instance;

    void Awake()
    {
        Players.Add(AreaPosition.Top, TopPlayer);
        Players.Add(AreaPosition.Low, LowPlayer);
        Instance = this;

        foreach (NeutralZoneController neutralBase in NeutralBases)
        {
            neutralBase.owner = AreaPosition.Neutral;
            neutralBase.SetOwnerColor(NeutralColor);
        }
    }
    void Start()
    {
        TopPlayer.playerColor = TopColor;
        LowPlayer.playerColor = LowColor;
        LowPlayer.tag = "LowPlayer";
        TopPlayer.tag = "TopPlayer";

        if (ConfirmTargetingButton != null)
            ConfirmTargetingButton.gameObject.SetActive(false);


        if (NetworkSessionData.IsNetworkSession == false)
        {
            localPlayer = LowPlayer;
            localPlayerDebugText.text = "Local Player: " + localPlayer.name;    
        }
        FogOfWarManager.Refresh();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            localPlayer = localPlayer == TopPlayer ? LowPlayer : TopPlayer;
            localPlayerDebugText.text = "Local Player: " + localPlayer.name;
            FogOfWarManager.Refresh();
            RefreshEndPhaseButtons();
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
