using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages BeginCombat phase effect resolution.
///
/// Flow (network):
///   StartSession → if selection effects with valid targets, highlights them; player clicks targets.
///   Player clicks End Phase (Accept) → ConfirmAndSubmit → steps through selection queue → submits to server.
///   Server waits for both players, merges, broadcasts canonical resolution → simultaneous execution.
///   IsComplete becomes true → AutoAdvanceFromBeginCombat advances to Battle.
///
/// Flow (local):
///   StartSession called for each player (TurnMaker foreach).
///   BeginLocalSelectionSession starts the sequential UI after all players are registered.
///   Players select targets one at a time; End Phase confirms each player's selections in order.
///   After all players confirm, all effects execute simultaneously.
///   IsComplete becomes true → AutoAdvanceFromBeginCombat advances.
/// </summary>
public static class BeginCombatEffectManager
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
    /// Returns true when the End Phase button should be blocked for this player during BeginCombat.
    /// Network: blocked when the current selection effect has no target chosen yet.
    /// Local: blocked when it is not this player's turn, or they haven't chosen a target yet.
    /// </summary>
    public static bool BlocksEndPhaseButton(Player player)
    {
        int playerIndex = System.Array.IndexOf(Player.Players, player);

        if (!NetworkSessionData.IsNetworkSession)
        {
            if (_confirmedPlayers.Contains(playerIndex)) return true;    // already confirmed
            if (_currentLocalPlayerIndex != playerIndex) return true;    // not this player's turn
        }

        return _selectionCursor < _selectionQueue.Count
            && _selectionQueue[_selectionCursor].SelectedTarget == null;
    }

    // =========================================================================
    // Phase lifecycle
    // =========================================================================

    /// <summary>Called by TurnManager.EnterPhase(BeginCombat) before TurnMakers are invoked.</summary>
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
    /// Called by TurnMaker.OnBeginCombatPhaseEntered for each player.
    /// Collects and stores effects; does not start the selection UI (BeginLocalSelectionSession does that).
    /// Network: once per client. Local: once per player, in TurnMaker foreach order.
    /// </summary>
    public static void StartSession(Player player)
    {
        List<PendingEffectSelection> effects = EffectProcessor.CollectAllBeginCombatEffects(player);
        // Effects with no eligible targets stay in effects (they fire on nobody = skip),
        // but are excluded from the selection queue so the player is not asked to pick a target.
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
                ShowCurrentSelectionWithChoice();
            else
                SubmitToServer(player); // nothing to select — auto-submit immediately
        }
    }

    /// <summary>
    /// Called by TurnManager.EnterPhase after all TurnMakers have run OnBeginCombatPhaseEntered.
    /// Starts the sequential selection UI for local mode. No-op in network mode.
    /// </summary>
    public static void BeginLocalSelectionSession()
    {
        if (NetworkSessionData.IsNetworkSession) return;
        if (_localPlayerQueue.Count > 0)
            LoadLocalPlayerSelections(_localPlayerQueue[0]);
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

        if (_localPlayerQueue.Count > 0)
        {
            LoadLocalPlayerSelections(_localPlayerQueue[0]);
        }
        else
        {
            // All players confirmed — execute all effects simultaneously
            ClearHighlights();
            foreach (List<PendingEffectSelection> playerEffects in _pendingPerPlayer.Values)
                ExecuteAll(playerEffects);
            _isComplete = true;
        }
    }

    // =========================================================================
    // Selection UI
    // =========================================================================

    /// <summary>Highlights eligible targets and marks the currently selected one (if any).</summary>
    private static void ShowCurrentSelectionWithChoice()
    {
        PendingEffectSelection currentSelection = _selectionQueue[_selectionCursor];
        
        foreach (KeyValuePair<int, CreatureLogic> creatureEntry in CreatureLogic.CreaturesCreatedThisGame)
        {
            CreatureLogic creature   = creatureEntry.Value;
            int           creatureID = creatureEntry.Key;
            bool          isEligible = currentSelection.EligibleTargets.Contains(creature);
            bool          isSelected = currentSelection.SelectedTarget == creature;

            GameObject creatureObject = IDHolder.GetGameObjectWithID(creatureID);
            creatureObject?.GetComponent<OneCreatureManager>()?.UpdateTargetableVisual(isEligible, isSelected);
        }
        
        foreach (KeyValuePair<int, BuildingLogic> buildingEntry in BuildingLogic.BuildingsCreatedThisGame)
        {
            BuildingLogic building = buildingEntry.Value;
            int           buildingID = buildingEntry.Key;
            bool          isEligible = currentSelection.EligibleTargets.Contains(building);
            bool          isSelected = currentSelection.SelectedTarget == building;

            GameObject buildingObject = IDHolder.GetGameObjectWithID(buildingID);
            buildingObject?.GetComponent<OneBuildingManager>()?.UpdateTargetableVisual(isEligible, isSelected);
        }
        
        GameObject sourceObject = IDHolder.GetGameObjectWithID(currentSelection.SourceEntityID);
        sourceObject?.GetComponent<OneCreatureManager>()?.UpdateUsingEffectVisual();

        // TODO: highlight bases, players when those target types are used

        TargetingVisualEvents.RaiseTargetingStarted(_selectionQueue, _selectionCursor);
    }

    private static void ClearHighlights()
    {
        TargetingVisualEvents.RaiseTargetingEnded();

        foreach (KeyValuePair<int, CreatureLogic> creatureEntry in CreatureLogic.CreaturesCreatedThisGame)
        {
            GameObject creatureObject = IDHolder.GetGameObjectWithID(creatureEntry.Key);
            creatureObject?.GetComponent<OneCreatureManager>()?.ClearTargetableVisual();
        }
    }

    /// <summary>
    /// Called by entity click handlers (OneCreatureManager, etc.) during BeginCombat.
    /// Records the player's target choice for the current selection. Does NOT submit.
    /// </summary>
    public static void OnEntityClicked(IIdentifiable clickedEntity)
    {
        Debug.Log($"Entity {clickedEntity.DisplayName} clicked");
        if (_selectionCursor >= _selectionQueue.Count) return;

        PendingEffectSelection currentSelection = _selectionQueue[_selectionCursor];
        if (!currentSelection.EligibleTargets.Contains(clickedEntity)) return;

        currentSelection.SelectedTarget = clickedEntity;
        Debug.Log($"Entity {clickedEntity.DisplayName} selected");
        ShowCurrentSelectionWithChoice();
        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
    }

    // =========================================================================
    // End Phase confirmation — called by TurnManager.RegisterEndPhase
    // =========================================================================

    /// <summary>
    /// Called when the player clicks End Phase during BeginCombat.
    /// Steps through the player's selection queue one at a time.
    /// Local: advances to the next player when done; executes all when every player has confirmed.
    /// Network: submits to server when all selections are confirmed.
    /// </summary>
    public static void ConfirmAndSubmit(Player player)
    {
        if (_isComplete) return;

        if (!NetworkSessionData.IsNetworkSession)
        {
            int playerIndex = System.Array.IndexOf(Player.Players, player);
            if (playerIndex != _currentLocalPlayerIndex) return; // not this player's turn

            if (_selectionCursor < _selectionQueue.Count)
            {
                PendingEffectSelection currentSelection = _selectionQueue[_selectionCursor];
                if (currentSelection.SelectedTarget == null) return; // defensive: button should be disabled

                _selectionCursor++;

                if (_selectionCursor < _selectionQueue.Count)
                {
                    ShowCurrentSelectionWithChoice();
                    return; // more selections for this player
                }
            }

            // This player's selections are all confirmed
            AdvanceLocalPlayer();
        }
        else
        {
            // Step through selection effects one at a time; submit only when all are confirmed.
            if (_selectionCursor < _selectionQueue.Count)
            {
                PendingEffectSelection currentSelection = _selectionQueue[_selectionCursor];
                if (currentSelection.SelectedTarget == null) return; // defensive: button should be disabled

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
            playerIndex, sourceEntityIDs, effectIndexes, selectedTargetIDs);
    }

    // =========================================================================
    // Network — receive canonical resolution from server
    // =========================================================================

    /// <summary>Called by GameNetworkManager.ApplyCanonicalEffectResolutionClientRpc on all clients.</summary>
    public static void ApplyCanonicalResolution(
        int[] sourceEntityIDs, int[] effectIndexes, int[] selectedTargetIDs)
    {
        for (int i = 0; i < sourceEntityIDs.Length; i++)
        {
            CardEffectData effectData = ResolveEffectData(
                sourceEntityIDs[i], effectIndexes[i], out EffectContext context);
            if (effectData == null || context == null) continue;

            context.SelectedTarget = ResolveEntityByID(selectedTargetIDs[i]); // null if id == -1
            EffectProcessor.ExecutePendingEffect(effectData, context);
        }
        _isComplete = true;
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

        foreach (Player player in Player.Players)
            if (player.ID == entityID) return player;

        return null;
    }
}