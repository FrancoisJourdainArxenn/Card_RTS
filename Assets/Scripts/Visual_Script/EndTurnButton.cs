using UnityEngine;

/// <summary>
/// Confirms end-of-phase for one participant. Uses <see cref="participantIndex"/> (slot in <see cref="Player.Players"/>).
/// Optionally resolves that index from <see cref="ownerPlayer"/> at Start.
/// </summary>
public class EndTurnButton : MonoBehaviour
{
    [Tooltip("Index in Player.Players (0 .. N-1). Used when resolveIndexFromOwner is false or resolution fails.")]
    public int participantIndex;

    [Tooltip("If set and resolveIndexFromOwner is true, participantIndex is overwritten in Start from the position of this player in Player.Players.")]
    public Player ownerPlayer;

    [Tooltip("If true, participantIndex is set from ownerPlayer's index in Player.Players at Start.")]
    public bool resolveIndexFromOwner = true;

    void Start()
    {
        if (!resolveIndexFromOwner || ownerPlayer == null || Player.Players == null)
            return;
        int idx = System.Array.IndexOf(Player.Players, ownerPlayer);
        if (idx >= 0)
            participantIndex = idx;
    }

    /// <summary>Player for UI (interactable, highlights). Null if index is out of range.</summary>
    public Player GetParticipantPlayer()
    {
        if (Player.Players == null || participantIndex < 0 || participantIndex >= Player.Players.Length)
            return null;
        return Player.Players[participantIndex];
    }

    public void OnClick()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.RegisterEndPhase(participantIndex);
            //Debug.Log(participantIndex);
    }
}
