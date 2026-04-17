using UnityEngine;

/// <summary>
/// Confirms end-of-phase for the local player.
/// A single instance in the scene suffices; it always resolves to GlobalSettings.localPlayer.
/// </summary>
public class EndTurnButton : MonoBehaviour
{
    /// <summary>Always returns the current local player.</summary>
    public Player GetParticipantPlayer()
    {
        return GlobalSettings.Instance?.localPlayer;
    }

    public void OnClick()
    {
        Player local = GlobalSettings.Instance?.localPlayer;
        if (TurnManager.Instance != null && local != null)
            TurnManager.Instance.RegisterEndPhase(local);
    }
}
