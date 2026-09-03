using System;
using System.Collections.Generic;

public class ZonePathLogic : IIdentifiable
{
    public int ID { get; }
    public string DisplayName => $"{ZoneA.DisplayName} <-> {ZoneB.DisplayName}";
    public ZoneLogic ZoneA { get; }
    public ZoneLogic ZoneB { get; }
    public bool IsLateral { get; private set; }
    public bool RequiresFlying { get; private set; }

    private IPathBlocker _blocker;
    public bool IsBlocked => _blocker != null && _blocker.IsActive;

    public event Action OnBlockedChanged;

    public ZonePathLogic(int id, ZoneLogic zoneA, ZoneLogic zoneB)
    {
        ID = id;
        ZoneA = zoneA;
        ZoneB = zoneB;
    }

    public void SetLateral(bool isLateral)
    {
        IsLateral = isLateral;
    }

    public void SetRequiresFlying(bool requiresFlying)
    {
        RequiresFlying = requiresFlying;
    }

    public void SetBlocker(IPathBlocker blocker)
    {
        if (_blocker != null)
            _blocker.OnDeactivated -= OnBlockerDeactivated;

        _blocker = blocker;

        if (_blocker != null)
            _blocker.OnDeactivated += OnBlockerDeactivated;

        OnBlockedChanged?.Invoke();
    }

    private void OnBlockerDeactivated()
    {
        _blocker = null;
        OnBlockedChanged?.Invoke();
    }

    public bool ConnectsTo(ZoneLogic zone) => ZoneA == zone || ZoneB == zone;

    public ZoneLogic OtherEnd(ZoneLogic from) => from == ZoneA ? ZoneB : ZoneA;

    public bool CanTraverse(Player player, ZoneLogic from, bool moverIsFlying = false)
    {
        if (RequiresFlying && !moverIsFlying) return false;
        if (IsBlocked) return false;
        if (IsLateral) return true;

        bool isForward = (player == GlobalSettings.Instance.LowPlayer)
            ? from == ZoneA
            : from == ZoneB;

        if (!isForward) return true;

        foreach (CreatureLogic creature in player.otherPlayer.playedCards.Creatures)
        {
            if (!from.SubZoneIDs.Contains(creature.BaseID)) continue;
            // Sur un path aérien, seules les créatures volantes ennemies bloquent l'avancée.
            if (RequiresFlying && !creature.IsFlying) continue;
            // Une créature avec 0 d'attaque ne bloque pas les déplacements adverses.
            if (creature.Attack <= 0) continue;
            return false;
        }
        return true;
    }
}
