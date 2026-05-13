using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages effect resolution for all phases (Regroup, Command, BeginCombat, End).
///
/// Flow (local):
///   ResetForNewPhase() called at phase entry.
///   StartSession called per player per TurnMaker callback — collects effects for one or more triggers.
///   BeginLocalSelectionSession starts the sequential UI after all players have registered.
///   If a player has selection effects: UI shown, player clicks target then End Phase to confirm.
///   If no selection effects: auto-advances immediately.
///   After all players confirm, all effects execute simultaneously → IsComplete = true.
///   The phase's auto-advance coroutine (or player-driven End Phase) then proceeds.
///
/// Flow (network — BeginCombat):
///   StartSession → if selection effects with valid targets, highlights them; player clicks targets.
///   Player clicks End Phase → ConfirmAndSubmit → steps through queue → submits to server.
///   Server waits for both players, merges, broadcasts canonical resolution → simultaneous execution.
///   IsComplete becomes true → AutoAdvanceFromBeginCombat advances to Battle.
/// </summary>
public static class EffectTargetingManager
{
    // Shared: active selection queue and cursor (reused for each player in turn)
    private static List<PendingEffectSelection> _selectionQueue = new List<PendingEffectSelection>();
    private static int _selectionCursor;

    // Network: all effects for the local player; prevents double-submission
    private static List<PendingEffectSelection> _allEffects = new List<PendingEffectSelection>();
    private static bool _hasSubmitted;

    // Both modes: effects per player index
    private static Dictionary<int, List<PendingEffectSelection>> _pendingPerPlayer
        = new Dictionary<int, List<PendingEffectSelection>>();
    private static HashSet<int> _confirmedPlayers = new HashSet<int>();

    // Local mode: sequential selection state
    private static int _currentLocalPlayerIndex = -1;
    private static List<int> _localPlayerQueue = new List<int>();
    private static Dictionary<int, List<PendingEffectSelection>> _localSelectionQueues
        = new Dictionary<int, List<PendingEffectSelection>>();

    private static bool _isComplete = true;
    public static bool IsComplete => _isComplete;

    /// <summary>
    /// Returns true when the End Phase button should be blocked for this player.
    /// Network: blocked when the current selection effect has no target chosen yet.
    /// Local: blocked when it is not this player's turn, or they haven't chosen a target yet.
    /// </summary>
    public static bool BlocksEndPhaseButton(Player player)
    {
        int playerIndex = System.Array.IndexOf(Player.Players, player);

        if (!NetworkSessionData.IsNetworkSession)
        {
            if (_confirmedPlayers.Contains(playerIndex))
                return true;
            if (playerIndex == _currentLocalPlayerIndex)
                return _selectionCursor < _selectionQueue.Count;
            // Other players: only blocked if they have their own selections to resolve later
            return _localSelectionQueues.TryGetValue(playerIndex, out var selectionQueue) && selectionQueue.Count > 0;
        }

        return _selectionCursor < _selectionQueue.Count;
    }

    /// <summary>
    /// True when this player has no remaining targeting selections.
    /// Unlike IsComplete, does not require all players to be done —
    /// lets a player resume normal phase actions while an opponent is still targeting.
    /// </summary>
    public static bool IsPlayerTargetingComplete(Player player)
    {
        if (_isComplete)
            return true;

        int playerIndex = System.Array.IndexOf(Player.Players, player);

        if (!NetworkSessionData.IsNetworkSession)
        {
            if (_confirmedPlayers.Contains(playerIndex))
                return true;
            if (!_localSelectionQueues.TryGetValue(playerIndex, out var sq) || sq.Count == 0)
                return true;
            if (playerIndex == _currentLocalPlayerIndex)
                return _selectionCursor >= _selectionQueue.Count;
            return false;
        }

        return player.MainPArea.AllowedToControlThisPlayer ? _hasSubmitted : _isComplete;
    }
    // =========================================================================
    // Phase lifecycle
    // =========================================================================

    /// <summary>Called by TurnManager.EnterPhase for every phase before TurnMakers are invoked.</summary>
    public static void ResetForNewPhase()
    {
        _allEffects.Clear();
        _selectionQueue.Clear();
        _selectionCursor      = 0;
        _pendingPerPlayer.Clear();
        _confirmedPlayers.Clear();
        _hasSubmitted         = false;
        _isComplete           = false;
        _currentLocalPlayerIndex = -1;
        _localPlayerQueue.Clear();
        _localSelectionQueues.Clear();
    }

    /// <summary>
    /// Called by TurnMaker phase callbacks for each player.
    /// Accepts one or more triggers so a single call can cover multiple trigger types (e.g. OnBattleEnd + OnEndTurn).
    /// Collects and stores effects; does not start the selection UI (BeginLocalSelectionSession does that).
    /// Network: once per client. Local: once per player, in TurnMaker foreach order.
    /// </summary>
    public static void StartSession(Player player, params TriggerType[] triggers)
    {
        List<PendingEffectSelection> effects = new();
        foreach (TriggerType trigger in triggers)
            effects.AddRange(EffectProcessor.StartPhaseCollectEffects(player, trigger));
        // Effects with no eligible targets stay in effects (they fire on nobody = skip),
        // but are excluded from the selection queue so the player is not asked to pick a target.
        foreach (PendingEffectSelection effect in effects)
        {
            string name = effect.Data.EffectName;
            string selectionChecked = effect.Data.RequiresPlayerInput ? "X" : " ";
            int targetCount = effect.EligibleTargets.Count;
            Debug.Log($"[{name}] Select [{selectionChecked}] - {targetCount} Eligible targets ");
        }
        List<PendingEffectSelection> selectionQueue = effects
            .Where(effect => effect.Data.RequiresPlayerInput && effect.EligibleTargets.Count > 0).ToList();

        int playerIndex = System.Array.IndexOf(Player.Players, player);
        _pendingPerPlayer[playerIndex] = effects;

        if (!NetworkSessionData.IsNetworkSession)
        {
            _localSelectionQueues[playerIndex] = selectionQueue;
            _localPlayerQueue.Add(playerIndex);
            // UI is started by BeginLocalSelectionSession after all players have registered
        }
        else
        {
            _allEffects      = effects;
            _selectionQueue  = selectionQueue;
            _selectionCursor = 0;

            if (_selectionQueue.Count > 0)
            {
                if (GameNetworkManager.Instance != null)
                {
                    int localIndex   = System.Array.IndexOf(Player.Players, player);
                    int[] sourceIDs  = _selectionQueue.Select(e => e.SourceEntityID).ToArray();
                    int[] effectIdxs = _selectionQueue.Select(e => e.EffectIndexInCard).ToArray();
                    GameNetworkManager.Instance.NotifyOpponentSelectingServerRpc(localIndex, sourceIDs, effectIdxs);
                }
                ShowCurrentSelectionWithChoice();
            }
            else
                SubmitToServer(player); // nothing to select — auto-submit immediately
        }
    }

    /// <summary>
    /// Called by TurnManager.EnterPhase after all TurnMakers have registered their sessions.
    /// Starts the sequential selection UI for local mode. No-op in network mode.
    /// If no player registered any effects, IsComplete is set to true immediately.
    /// </summary>
    public static void BeginLocalSelectionSession()
    {
        if (NetworkSessionData.IsNetworkSession)
        {
            if (_pendingPerPlayer.Count == 0)
                _isComplete = true;
            if (GlobalSettings.Instance != null)
                GlobalSettings.Instance.RefreshEndPhaseButtons();
            return;
        }
        if (_localPlayerQueue.Count > 0)
            LoadLocalPlayerSelections(_localPlayerQueue[0]);
        else
            _isComplete = true; // Aucun effet à résoudre pour cette phase
        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
    }

    // =========================================================================
    // Local mode — sequential player processing
    // =========================================================================

    private static void LoadLocalPlayerSelections(int playerIndex)
    {
        _currentLocalPlayerIndex = playerIndex;
        _selectionQueue = _localSelectionQueues.TryGetValue(playerIndex, out List<PendingEffectSelection> sq)
            ? sq : new List<PendingEffectSelection>();
        _selectionCursor = 0;

        Debug.Log($"[Selection] : {_selectionQueue.Count}");
        if (_selectionQueue.Count > 0)
            ShowCurrentSelectionWithChoice();
        else
            AdvanceLocalPlayer(); // no selections for this player — move on immediately

        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
    }

    private static void AdvanceLocalPlayer()
    {
        if (_localPlayerQueue.Count > 0)
        {
            _confirmedPlayers.Add(_currentLocalPlayerIndex);
            _localPlayerQueue.RemoveAt(0);
        }

        // Skip players who pre-confirmed while another player was selecting
        while (_localPlayerQueue.Count > 0 && _confirmedPlayers.Contains(_localPlayerQueue[0]))
            _localPlayerQueue.RemoveAt(0);

        if (_localPlayerQueue.Count > 0)
        {
            LoadLocalPlayerSelections(_localPlayerQueue[0]);
        }
        else
        {
            // All players confirmed — execute all effects simultaneously
            ClearHighlights();
            TargetingVisualEvents.RaiseEffectsExecuting();
            foreach (List<PendingEffectSelection> playerEffects in _pendingPerPlayer.Values)
                ExecuteAll(playerEffects);
            _isComplete = true;
            TurnManager.RefreshAllPlayableHighlights();
        }
    }

    // =========================================================================
    // Selection UI
    // =========================================================================

    /// <summary>Highlights eligible targets and marks the currently selected one (if any).</summary>
    private static void ShowCurrentSelectionWithChoice()
    {
        PendingEffectSelection currentSelection = _selectionQueue[_selectionCursor];
        Debug.Log($"[Update Visual Selection] : {currentSelection.Data.EffectName} by {currentSelection.Context.Source.DisplayName}");

        foreach (KeyValuePair<int, CreatureLogic> e in CreatureLogic.CreaturesCreatedThisGame)
            UpdateEntityTargetableVisual(e.Key, e.Value, currentSelection);

        foreach (KeyValuePair<int, BuildingLogic> e in BuildingLogic.BuildingsCreatedThisGame)
            UpdateEntityTargetableVisual(e.Key, e.Value, currentSelection);

        foreach (KeyValuePair<int, BaseLogic> e in BaseLogic.BasesCreatedThisGame)
        {
            if (!e.Value.IsHomeBase)
                UpdateEntityTargetableVisual(e.Key, e.Value, currentSelection);
        }

        foreach (Player player in Player.Players)
        {
            BaseLogic.BasesCreatedThisGame.TryGetValue(player.PlayerID, out BaseLogic homeBase);
            bool isEligible = currentSelection.EligibleTargets.Contains(player)
                           || (homeBase != null && currentSelection.EligibleTargets.Contains(homeBase));
            bool isSelected = currentSelection.SelectedTarget == player
                           || (homeBase != null && currentSelection.SelectedTarget == homeBase);
            IDHolder.GetGameObjectWithID(player.ID)
                ?.GetComponent<ITargetableVisual>()
                ?.UpdateTargetableVisual(isEligible, isSelected);
        }

        // TODO: highlight bases, players when those target types are used

        TargetingVisualEvents.RaiseTargetingStarted(_selectionQueue, _selectionCursor);
        foreach (ZoneManager zone in ZoneManager.AllZones)
            zone.UpdateTargetableVisual(
                currentSelection.EligibleTargets.Contains(zone.Logic),
                currentSelection.SelectedTarget == zone.Logic);

        IDHolder.GetGameObjectWithID(currentSelection.SourceEntityID)
            ?.GetComponent<OneLivableManager>()?.UpdateUsingEffectVisual();

    }

    private static void UpdateEntityTargetableVisual(int id, IIdentifiable entity, PendingEffectSelection selection)
    {
        IDHolder.GetGameObjectWithID(id)
            ?.GetComponent<ITargetableVisual>()
            ?.UpdateTargetableVisual(
                selection.EligibleTargets.Contains(entity),
                selection.SelectedTarget == entity);
    }

    private static void ClearHighlights()
    {
        TargetingVisualEvents.RaiseTargetingEnded();

        foreach (int id in CreatureLogic.CreaturesCreatedThisGame.Keys)
            ClearEntityVisual(id);
        foreach (int id in BuildingLogic.BuildingsCreatedThisGame.Keys)
            ClearEntityVisual(id);
        foreach (KeyValuePair<int, BaseLogic> e in BaseLogic.BasesCreatedThisGame.Where(e => !e.Value.IsHomeBase))
            ClearEntityVisual(e.Key);
        foreach (Player player in Player.Players)
            ClearEntityVisual(player.ID);
        foreach (ZoneManager zone in ZoneManager.AllZones)
            zone.ClearTargetableVisual();
    }

    private static void ClearEntityVisual(int id)
    {
        IDHolder.GetGameObjectWithID(id)?.GetComponent<ITargetableVisual>()?.ClearTargetableVisual();
    }

    /// <summary>
    /// Called by entity click handlers (OneCreatureManager, etc.) when a targeting session is active.
    /// Records the player's target choice for the current selection. Does NOT submit.
    /// </summary>
    public static bool OnEntityClicked(IIdentifiable clickedEntity)
    {
        Debug.Log($"Entity {clickedEntity.DisplayName} clicked");
        if (_selectionCursor >= _selectionQueue.Count) return false;

        PendingEffectSelection currentSelection = _selectionQueue[_selectionCursor];
        if (!currentSelection.EligibleTargets.Contains(clickedEntity)) return false;

        currentSelection.SelectedTarget = clickedEntity;
        Debug.Log($"Entity {clickedEntity.DisplayName} selected");
        TargetingVisualEvents.RaiseTargetingStarted(_selectionQueue, _selectionCursor);
        _selectionCursor++;

        if (_selectionCursor < _selectionQueue.Count)
            ShowCurrentSelectionWithChoice();
        else if (!NetworkSessionData.IsNetworkSession)
            AdvanceLocalPlayer();
        else
            ClearHighlights();

        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
        return true;
    }


    // =========================================================================
    // End Phase confirmation — called by TurnManager.RegisterEndPhase
    // =========================================================================

    /// <summary>
    /// Called when the player clicks End Phase while a targeting session is active.
    /// Steps through the player's selection queue one at a time.
    /// Local: advances to the next player when done; executes all when every player has confirmed.
    /// Network (BeginCombat): submits to server when all selections are confirmed.
    /// </summary>
    public static void ConfirmAndSubmit(Player player)
    {
        if (_isComplete)
            return;

        if (!NetworkSessionData.IsNetworkSession)
        {
            int playerIndex = System.Array.IndexOf(Player.Players, player);
            if (_confirmedPlayers.Contains(playerIndex))
                return;

            if (playerIndex == _currentLocalPlayerIndex)
            {
                if (_selectionCursor < _selectionQueue.Count)
                {
                    PendingEffectSelection currentSelection = _selectionQueue[_selectionCursor];
                    if (currentSelection.SelectedTarget == null)
                        return;

                    _selectionCursor++;

                    if (_selectionCursor < _selectionQueue.Count)
                    {
                        ShowCurrentSelectionWithChoice();
                        return;
                    }
                }

                AdvanceLocalPlayer();
            }
            else
            {
                // Player with no pending selections pre-confirms while another player is selecting
                bool hasOwnSelections = _localSelectionQueues.TryGetValue(playerIndex, out var sq) && sq.Count > 0;
                if (hasOwnSelections)
                    return; // Has own selections — sequential, must wait for their turn
                _confirmedPlayers.Add(playerIndex);
                if (GlobalSettings.Instance != null)
                    GlobalSettings.Instance.RefreshEndPhaseButtons();
            }
        }
        else
        {
            // Step through selection effects one at a time; submit only when all are confirmed.
            if (_selectionCursor < _selectionQueue.Count)
            {
                PendingEffectSelection currentSelection = _selectionQueue[_selectionCursor];
                if (currentSelection.SelectedTarget == null)
                    return; // defensive: button should be disabled

                _selectionCursor++;

                if (_selectionCursor < _selectionQueue.Count)
                {
                    ShowCurrentSelectionWithChoice();
                    return;
                }
                // All selections confirmed — fall through to submit
            }

            ClearHighlights();
            SubmitToServer(player);
        }
    }

    // =========================================================================
    // Effect execution
    // =========================================================================

    private static void ExecuteAll(List<PendingEffectSelection> effects)
    {
        foreach (PendingEffectSelection pendingEffect in effects)
        {
            // SelectedTarget is null for auto-fire effects — GetExecutionTargets handles that correctly
            pendingEffect.Context.SelectedTarget = pendingEffect.SelectedTarget;
            EffectProcessor.ExecutePendingEffect(pendingEffect.Data, pendingEffect.Context);
        }
    }

    // =========================================================================
    // Network — submit to server
    // =========================================================================

    private static void SubmitToServer(Player player)
    {
        if (_hasSubmitted) return; // guard against double-submission
        _hasSubmitted = true;

        int playerIndex = System.Array.IndexOf(Player.Players, player);
        int effectCount = _allEffects.Count;

        int[] sourceEntityIDs   = new int[effectCount];
        int[] effectIndexes     = new int[effectCount];
        int[] selectedTargetIDs = new int[effectCount];

        for (int i = 0; i < effectCount; i++)
        {
            sourceEntityIDs[i]   = _allEffects[i].SourceEntityID;
            effectIndexes[i]     = _allEffects[i].EffectIndexInCard;
            selectedTargetIDs[i] = _allEffects[i].SelectedTarget?.ID ?? -1; // -1 = auto-fire, no target
        }

        GameNetworkManager.Instance.SubmitEffectTargetsServerRpc(
            playerIndex, TurnManager.Instance.CurrentPhase, sourceEntityIDs, effectIndexes, selectedTargetIDs);
    }

    // =========================================================================
    // Network — receive canonical resolution from server
    // =========================================================================

    /// <summary>Called by GameNetworkManager.ApplyCanonicalEffectResolutionClientRpc on all clients.</summary>
    public static void ApplyCanonicalResolution(
        int[] sourceEntityIDs, int[] effectIndexes, int[] selectedTargetIDs, int effectSeed)
    {
        // Pré-générer un seed isolé pour chaque effet, avant d'en exécuter un seul.
        // Si un effet ne s'exécute pas sur un client, ça n'affecte pas les seeds des autres.
        System.Random masterRng = new System.Random(effectSeed);
        int[] subSeeds = new int[sourceEntityIDs.Length];
        for (int i = 0; i < subSeeds.Length; i++)
            subSeeds[i] = masterRng.Next();

        for (int i = 0; i < sourceEntityIDs.Length; i++)
        {
            TargetingVisualEvents.RaiseEffectsExecuting();
            CardEffectData effectData = ResolveEffectData(
                sourceEntityIDs[i], effectIndexes[i], out EffectContext context);
            if (effectData == null || context == null) continue;

            context.SelectedTarget = ResolveEntityByID(selectedTargetIDs[i]);

            EffectSO.SetNetworkRng(new System.Random(subSeeds[i]));
            EffectProcessor.ExecutePendingEffect(effectData, context);
            EffectSO.ClearNetworkRng();
        }

        _isComplete = true;
        TurnManager.RefreshAllPlayableHighlights();
        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
    }

    // =========================================================================
    // ID resolution helpers
    // =========================================================================

    private static CardEffectData ResolveEffectData(
        int sourceEntityID, int effectIndex, out EffectContext context)
    {
        context = null;

        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(sourceEntityID, out CreatureLogic sourceCreature))
        {
            bool indexIsValid = sourceCreature.ca.Effects != null
                && effectIndex >= 0 && effectIndex < sourceCreature.ca.Effects.Count;
            if (!indexIsValid) return null;

            context = new EffectContext { Caster = sourceCreature.owner, Source = sourceCreature };
            return sourceCreature.ca.Effects[effectIndex];
        }

        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(sourceEntityID, out BuildingLogic sourceBuilding))
        {
            bool indexIsValid = sourceBuilding.ca.Effects != null
                && effectIndex >= 0 && effectIndex < sourceBuilding.ca.Effects.Count;
            if (!indexIsValid) return null;

            context = new EffectContext { Caster = sourceBuilding.owner, Source = sourceBuilding };
            return sourceBuilding.ca.Effects[effectIndex];
        }

        return null;
    }

    private static IIdentifiable ResolveEntityByID(int entityID)
    {
        if (entityID < 0) return null;

        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(entityID, out CreatureLogic creature))
            return creature;
        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(entityID, out BuildingLogic building))
            return building;
        if (BaseLogic.BasesCreatedThisGame.TryGetValue(entityID, out BaseLogic playerBase))
            return playerBase;

        foreach (ZoneManager zone in ZoneManager.AllZones)
            if (zone.Logic.ID == entityID) return zone.Logic;

        foreach (Player player in Player.Players)
            if (player.ID == entityID) return player;

        return null;
    }

    public static CardAsset GetTokenAsset(int sourceEntityID, int effectIndex)
    {
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(sourceEntityID, out CreatureLogic creature))
        {
            if (creature.ca?.Effects != null && effectIndex >= 0 && effectIndex < creature.ca.Effects.Count)
                return creature.ca.Effects[effectIndex].Parameters.TokenToSummon;
        }
        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(sourceEntityID, out BuildingLogic building))
        {
            if (building.ca?.Effects != null && effectIndex >= 0 && effectIndex < building.ca.Effects.Count)
                return building.ca.Effects[effectIndex].Parameters.TokenToSummon;
        }
        return null;
    }

}