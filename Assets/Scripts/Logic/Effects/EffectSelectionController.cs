using System.Collections.Generic;

/// <summary>
/// Gère la session de sélection active : file d'effets, position du curseur,
/// clicks sur les entités et mise à jour des highlights.
/// Ne connaît ni les joueurs, ni les phases, ni le réseau, ni l'exécution.
/// </summary>
public static class EffectSelectionController
{
    private static List<PendingEffectSelection> _queue = new List<PendingEffectSelection>();
    private static int _cursor;

    /// <summary>True si le curseur pointe encore sur un effet à résoudre.</summary>
    public static bool HasPendingSelection => _cursor < _queue.Count;

    /// <summary>True si la file est non-vide et que toutes les sélections sont faites.</summary>
    public static bool IsAllSelected => _queue.Count > 0 && _cursor >= _queue.Count;

    private static PendingEffectSelection Current => HasPendingSelection ? _queue[_cursor] : null;

    // ── Gestion de session ────────────────────────────────────────────────────

    public static void Reset()
    {
        _queue.Clear();
        _cursor = 0;
        ClearHighlights();
    }

    public static void LoadQueue(List<PendingEffectSelection> effects)
    {
        _queue  = effects ?? new List<PendingEffectSelection>();
        _cursor = 0;

        if (HasPendingSelection)
            ShowCurrentHighlights();
    }

    // ── Input joueur ──────────────────────────────────────────────────────────

    /// <summary>Enregistre la cible cliquée. Retourne true si le click est accepté.</summary>
    public static bool OnEntityClicked(IIdentifiable clicked)
    {
        if (!HasPendingSelection)
            return false;

        if (!Current.EligibleTargets.Contains(clicked))
            return false;

        Current.SelectedTarget = clicked;
        TargetingVisualEvents.RaiseTargetingStarted(_queue, _cursor);
        _cursor++;

        if (HasPendingSelection)
            ShowCurrentHighlights();
        else
            ClearHighlights();

        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();

        return true;
    }

    /// <summary>
    /// Tente d'avancer le curseur (appelé à la confirmation du joueur).
    /// Retourne true si toutes les sélections sont terminées.
    /// </summary>
    public static bool Advance()
    {
        if (!HasPendingSelection)
            return true;

        if (Current.SelectedTarget == null)
            return false;

        _cursor++;

        if (HasPendingSelection)
            ShowCurrentHighlights();
        else
            ClearHighlights();

        return !HasPendingSelection;
    }

    // ── Highlights ────────────────────────────────────────────────────────────

    private static void ShowCurrentHighlights()
    {
        PendingEffectSelection sel = Current;
        TargetingVisualEvents.RaiseTargetingStarted(_queue, _cursor);

        foreach (KeyValuePair<int, CreatureLogic> e in CreatureLogic.CreaturesCreatedThisGame)
            UpdateEntityVisual(e.Key, e.Value, sel);

        foreach (KeyValuePair<int, BuildingLogic> e in BuildingLogic.BuildingsCreatedThisGame)
            UpdateEntityVisual(e.Key, e.Value, sel);

        foreach (KeyValuePair<int, BaseLogic> e in BaseLogic.BasesCreatedThisGame)
        {
            if (!e.Value.IsHomeBase)
                UpdateEntityVisual(e.Key, e.Value, sel);
        }

        foreach (Player player in Player.Players)
        {
            BaseLogic.BasesCreatedThisGame.TryGetValue(player.PlayerID, out BaseLogic homeBase);

            bool eligible = sel.EligibleTargets.Contains(player)
                         || (homeBase != null && sel.EligibleTargets.Contains(homeBase));
            bool selected = sel.SelectedTarget == player
                         || (homeBase != null && sel.SelectedTarget == homeBase);

            IDHolder.GetGameObjectWithID(player.ID)
                ?.GetComponent<ITargetableVisual>()
                ?.UpdateTargetableVisual(eligible, selected);
        }

        foreach (ZoneManager zone in ZoneManager.AllZones)
            zone.UpdateTargetableVisual(
                sel.EligibleTargets.Contains(zone.Logic),
                sel.SelectedTarget == zone.Logic);

        IDHolder.GetGameObjectWithID(sel.SourceEntityID)
            ?.GetComponent<OneLivableManager>()?.UpdateUsingEffectVisual();
    }

    public static void RefreshCurrentHighlights()
    {
        if (HasPendingSelection)
            ShowCurrentHighlights();
    }

    public static void ClearHighlights()
    {
        TargetingVisualEvents.RaiseTargetingEnded();

        foreach (int id in CreatureLogic.CreaturesCreatedThisGame.Keys)
            ClearEntityVisual(id);

        foreach (int id in BuildingLogic.BuildingsCreatedThisGame.Keys)
            ClearEntityVisual(id);

        foreach (KeyValuePair<int, BaseLogic> e in BaseLogic.BasesCreatedThisGame)
        {
            if (!e.Value.IsHomeBase)
                ClearEntityVisual(e.Key);
        }

        foreach (Player player in Player.Players)
            ClearEntityVisual(player.ID);

        foreach (ZoneManager zone in ZoneManager.AllZones)
            zone.ClearTargetableVisual();
    }

    private static void UpdateEntityVisual(int id, IIdentifiable entity, PendingEffectSelection sel)
        => IDHolder.GetGameObjectWithID(id)
            ?.GetComponent<ITargetableVisual>()
            ?.UpdateTargetableVisual(
                sel.EligibleTargets.Contains(entity),
                sel.SelectedTarget == entity);

    private static void ClearEntityVisual(int id)
        => IDHolder.GetGameObjectWithID(id)
            ?.GetComponent<ITargetableVisual>()
            ?.ClearTargetableVisual();
}
