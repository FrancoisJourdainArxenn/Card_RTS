using UnityEngine;
using TMPro;

public class EndTurnButton : MonoBehaviour
{
    [Tooltip("Solo only: assign the specific player this button controls. Leave null to use localPlayer (multiplayer default).")]
    public Player assignedPlayer;

    public string labelEndTurn = "End Phase";
    public string labelConfirm = "Confirm";

    public Player GetParticipantPlayer()
    {
        return assignedPlayer != null ? assignedPlayer : GlobalSettings.Instance?.localPlayer;
    }

    public void SetLabel(bool isConfirmMode)
    {
        TMP_Text label = GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = isConfirmMode ? labelConfirm : labelEndTurn;
    }

    public void OnClick()
    {
        Player p = GetParticipantPlayer();
        if (TurnManager.Instance != null && p != null)
            TurnManager.Instance.RegisterEndPhase(p);
    }
}
