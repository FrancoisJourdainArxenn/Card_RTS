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
    
    [Header("Colors")]
    public Color32 TopColor;
    public Color32 LowColor;
    public Color32 NeutralColor;

    [Header("Neutral Base")]
    public NeutralBaseController[] NeutralBases;
    

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
    [Header("Other")]
    [Tooltip("End phase button for the low-area human player.")]
    public Button EndTurnButton;
    [Tooltip("End phase button for the top-area human player (assign in the scene).")]
    public Button EndPhaseButtonTopPlayer;
    public GameObject GameOverPanel;
    public TMP_Text activePlayerDebugText;

    public Dictionary<AreaPosition, Player> Players = new Dictionary<AreaPosition, Player>();
    public Player activePlayer;

    public static GlobalSettings Instance;

    void Awake()
    {
        Players.Add(AreaPosition.Top, TopPlayer);
        Players.Add(AreaPosition.Low, LowPlayer);
        Instance = this;

        foreach (NeutralBaseController neutralBase in NeutralBases)
        {
            neutralBase.owner = AreaPosition.Neutral;
            neutralBase.SetOwnerColor(NeutralColor);
        }
    }


    void Start()
    {
        TopPlayer.playerColor = TopColor;
        LowPlayer.playerColor = LowColor;
        activePlayer = LowPlayer;
        activePlayerDebugText.text = "Active Player: " + activePlayer.name;
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
        foreach (EndTurnButton eb in Object.FindObjectsByType<EndTurnButton>(FindObjectsSortMode.None))
        {
            Button btn = eb.GetComponent<Button>();
            if (btn == null)
                continue;
            Player player = eb.GetParticipantPlayer();
            SetEndPhaseButtonState(btn, player);
        }
    }

    static void SetEndPhaseButtonState(Button button, Player player)
    {
        if (button == null || player == null)
            return;
        bool human = player.MainPArea.AllowedToControlThisPlayer;
        bool gameActive = player.MainPArea.ControlsON;
        bool notYetReady = !TurnManager.Instance.HasPlayerRegisteredEndPhase(player);
        button.interactable = human && gameActive && notYetReady;
    }

}
