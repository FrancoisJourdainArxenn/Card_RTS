using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ScanButton : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private HoverArrow hoverArrow;
    [SerializeField] private GameObject presenceNotificationVFX;
    [SerializeField] private UITooltipTrigger scanTooltipTrigger;

    public int scanCost = 2;
    public TMP_Text scanCostText;
    private int currentScanCost;
    private bool scanedThisTurn = false;
    [SerializeField] private Button button;
    [SerializeField] private Image radarBG;
    [SerializeField] private Image radarLine;
    [SerializeField] private Color disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    private Color _sprite1NormalColor;
    private Color _sprite2NormalColor;



    private static ScanButton _instance;
    private static ScanButton _activeSession;
    public static bool IsActive => _activeSession != null;
    private int _sessionStartFrame = -1;


    void Awake()
    {
        _instance = this;
        currentScanCost = scanCost;
        _sprite1NormalColor = radarBG.color;
        _sprite2NormalColor = radarLine.color;

        RefreshCostText();

        if (scanTooltipTrigger != null)
        {
            scanTooltipTrigger.SetDynamicText(() =>
                $"Spend {currentScanCost} to reveal a zone.\n" +
                $"Each turn, the amount to spend reduces by 1. \n" +
                $"The amount Resets after use.");
        }
    }

    void Update()
    {
        if (_activeSession != this) return;
        if (Draggable.DraggingThis != null)
            CancelSelection();
    }

    void LateUpdate()
    {
        if (_activeSession != this) return;
        if (Time.frameCount == _sessionStartFrame) return;
        if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
            CancelSelection();
    }

    void OnEnable()
    {
        TurnManager.OnRoundStart += ClearRevealedZones;
        TargetingVisualEvents.OnTargetingStarted += OnEffectTargetingStarted;
    }

    void OnDisable()
    {
        TurnManager.OnRoundStart -= ClearRevealedZones;
        TargetingVisualEvents.OnTargetingStarted -= OnEffectTargetingStarted;
    }

    private void OnEffectTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
    {
        if (_activeSession != null)
            CancelSelection();
    }


    public void OnPointerClick(PointerEventData eventData)
    {
        if (scanedThisTurn) return;
        if (_activeSession != null)
        {
            CancelSelection();
            return;
        }
        StartSelection();
    }

    public static bool HandleZoneClickIfActive(ZoneManager zone, PointerEventData eventData)
    {
        if (_activeSession == null) return false;
        if (eventData.button == PointerEventData.InputButton.Right)
            _activeSession.CancelSelection();
        else
            _activeSession.OnZoneSelected(zone);
        return true;
    }

    public static void ReceiveRevealNotification(ZoneManager zone)
    {
        if (_instance == null || _instance.presenceNotificationVFX == null) return;
        GameObject vfx = Instantiate(_instance.presenceNotificationVFX, zone.transform.position, Quaternion.Euler(-90f, 0f, 0f));
        Destroy(vfx, 3f);
    }

    void StartSelection()
    {
        if (GlobalSettings.Instance.localPlayer.MainRessourceAvailable < currentScanCost)
        {
            new ShowMessageCommand("Insufficient Ressources", 1.5f).AddToQueue();
            return;
        }
        if (scanedThisTurn)
        {
            new ShowMessageCommand("Scan already used this turn", 1.5f).AddToQueue();
            return;
        }
        _activeSession = this;
        _sessionStartFrame = Time.frameCount;
        foreach (ZoneManager zone in ZoneManager.AllZones)
        {
            bool noPresence = FogOfWarManager.Instance == null || FogOfWarManager.Instance.IsZoneFogged(zone);
            Debug.Log($"Zone {zone.name} — noPresence={noPresence}, overlay={(zone != null ? "ok" : "null")}");
            if (noPresence)
                zone.UpdateTargetableVisual(true);
        }
        if (hoverArrow != null) hoverArrow.ShowToMouse();
    }


    void OnZoneSelected(ZoneManager zone)
    {
        Player localPlayer = GlobalSettings.Instance.localPlayer;
        localPlayer.MainRessourceAvailable -= currentScanCost;
        if (currentScanCost < scanCost)
            currentScanCost = scanCost;        
        else
            currentScanCost += 1;
        SetUsed(true);
        Debug.Log($"Scan now Costs :" + currentScanCost);
        RefreshCostText();

        zone.UpdateTargetableVisual(false, true);
        ApplyReveal(zone.Logic.ID);
        if (NetworkSessionData.IsNetworkSession)
        {
            int playerIndex = Array.IndexOf(Player.Players, GlobalSettings.Instance.localPlayer);
            GameNetworkManager.Instance.NotifyZoneRevealServerRpc(zone.Logic.ID);
        }
            else
        {
            ReceiveRevealNotification(zone);
        }
        EndSelection();
    }

    void CancelSelection()
    {
        EndSelection();
    }

    void EndSelection()
    {
        _activeSession = null;
        foreach (ZoneManager zone in ZoneManager.AllZones)
            zone.ClearTargetableVisual();
        if (hoverArrow != null)
            hoverArrow.Hide();
    }

    static void ApplyReveal(int zoneID)
    {
        FogOfWarManager.ForceRevealedZones.Add(zoneID);
        FogOfWarManager.Refresh();
    }

    static void ClearRevealedZones()
    {
        FogOfWarManager.ForceRevealedZones.Clear();
        FogOfWarManager.Refresh();            
        _instance?.SetUsed(false);
    }

    static void RefreshCostText()
    {
        _instance.scanCostText.text = _instance.currentScanCost.ToString();

    }

    void SetUsed(bool used)
    {
        if(!used &&!scanedThisTurn && currentScanCost > 0)
            currentScanCost += -1;
        scanedThisTurn = used;
        button.interactable = !used;
        radarBG.color = used ? disabledColor : _sprite1NormalColor;
        radarLine.color = used ? disabledColor : _sprite2NormalColor;
        RefreshCostText();
    }

}
