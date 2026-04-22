using UnityEngine;

public class EndTurnButton : MonoBehaviour
{
    [Tooltip("Solo only: assign the specific player this button controls. Leave null to use localPlayer (multiplayer default).")]
    public Player assignedPlayer;

    public Player GetParticipantPlayer()
    {
        return assignedPlayer != null ? assignedPlayer : GlobalSettings.Instance?.localPlayer;
    }

    public void OnClick()
    {
        Player p = GetParticipantPlayer();
        if (TurnManager.Instance != null && p != null)
            TurnManager.Instance.RegisterEndPhase(p);
    }
}
