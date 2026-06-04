using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Orchestrateur du pipeline d'effets de phase : collecte → sélection → résolution.
///
/// Local :  ResetForNewPhase → StartSession (×N joueurs) → BeginLocalSelectionSession
///          → sélection séquentielle via EffectSelectionController → ExecuteAll → IsComplete
///
/// Réseau : ResetForNewPhase → StartSession (joueur local seul) → BeginLocalSelectionSession
///          → soumission serveur → ApplyCanonicalResolution → IsComplete
/// </summary>
public static class PhaseEffectPipeline
{
    private static bool _isComplete;
    public static bool IsComplete => _isComplete;

    // État local : file des joueurs à traiter séquentiellement
    private static int          _currentLocalPlayerIndex = -1;
    private static List<int>    _localPlayerQueue        = new List<int>();
    private static HashSet<int> _confirmedPlayers        = new HashSet<int>();

    // État réseau
    private static bool _hasSubmitted;

    // Compteur de génération : protège contre les appels onComplete périmés après ResetForNewPhase.
    private static int _generation = 0;

    // ── Cycle de phase ────────────────────────────────────────────────────────

    public static void ResetForNewPhase()
    {
        _generation++;
        List<PendingEffectSelection> stale = EffectStack.GetAllEffects();
        // if (stale.Count > 0)
        //     Debug.Log($"[Pipeline] ResetForNewPhase — stack non vide avant reset ({stale.Count} effet(s)): {string.Join(", ", stale.Select(e => $"{e.Data.EffectName}({e.Data.Trigger})"))}");

        EffectStack.Reset();
        EffectSelectionController.Reset();
        _confirmedPlayers.Clear();
        _localPlayerQueue.Clear();
        _currentLocalPlayerIndex = -1;
        _hasSubmitted            = false;
        _isComplete              = false;
        // Debug.Log("[Pipeline] ResetForNewPhase — tout l'état réinitialisé");
    }

    /// <summary>
    /// Appelé par TurnMaker pour chaque joueur à l'entrée de phase.
    /// Collecte les effets via EffectRegistry et les stocke dans EffectStack.
    /// Réseau : appelé une seule fois pour le joueur local ; soumission automatique si aucune sélection.
    /// </summary>
    public static void StartSession(Player player, params TriggerType[] triggers)
    {
        int playerIndex = GetIndex(player);

        List<PendingEffectSelection> effects = new List<PendingEffectSelection>();
        foreach (TriggerType trigger in triggers)
            effects.AddRange(EffectRegistry.CollectPhaseEffects(player, trigger));

        List<PendingEffectSelection> selectionEffects = effects
            .Where(e => e.Data.RequiresPlayerInput && e.EligibleTargets.Count > 0)
            .ToList();

        // Debug.Log($"[Pipeline] StartSession — joueur {playerIndex} ({player?.name}) | {effects.Count} effet(s) dont {selectionEffects.Count} avec sélection | triggers: {string.Join(", ", triggers)}");

        // foreach (PendingEffectSelection e in effects)
        //     Debug.Log($"[Pipeline]   effet: {e.Data.EffectName} | Input [{(e.Data.RequiresPlayerInput ? "X" : " ")}] | Cibles éligibles: {e.EligibleTargets.Count}");

        EffectStack.AddFor(playerIndex, effects);

        if (!NetworkSessionData.IsNetworkSession)
        {
            _localPlayerQueue.Add(playerIndex);
        }
        else
        {
            if (selectionEffects.Count > 0)
            {
                // Debug.Log($"[Pipeline] StartSession (réseau) — {selectionEffects.Count} effet(s) à sélectionner, chargement queue UI");
                if (GameNetworkManager.Instance != null)
                    GameNetworkManager.Instance.NotifyOpponentSelectingServerRpc(
                        playerIndex,
                        selectionEffects.Select(e => e.SourceEntityID).ToArray(),
                        selectionEffects.Select(e => e.EffectIndexInCard).ToArray());

                EffectSelectionController.LoadQueue(selectionEffects);
            }
            else
            {
                // Debug.Log($"[Pipeline] StartSession (réseau) — aucune sélection requise, auto-soumission");
                SubmitToServer(player);
            }
        }
    }

    /// <summary>
    /// Appelé en fin de TurnManager.EnterPhase, après tous les StartSession.
    /// Lance la sélection séquentielle en local ; en réseau, marque IsComplete si rien n'est enregistré.
    /// </summary>
    public static void BeginLocalSelectionSession()
    {
        // Debug.Log($"[Pipeline] BeginLocalSelectionSession — réseau={NetworkSessionData.IsNetworkSession} | queueLocale=[{string.Join(", ", _localPlayerQueue)}]");

        if (NetworkSessionData.IsNetworkSession)
        {
            if (EffectStack.IsEmpty)
                _isComplete = true;

            GlobalSettings.Instance?.RefreshEndPhaseButtons();
            return;
        }

        if (_localPlayerQueue.Count > 0)
        {
            LoadLocalPlayerSelections(_localPlayerQueue[0]);
        }
        else
        {
            _isComplete = true;
        }

        GlobalSettings.Instance?.RefreshEndPhaseButtons();
    }

    // ── Input joueur ──────────────────────────────────────────────────────────

    public static bool OnEntityClicked(IIdentifiable entity)
    {
        if (_isComplete)
            return false;

        return EffectSelectionController.OnEntityClicked(entity);
    }

    /// <summary>
    /// Appelé par TurnManager.RegisterEndPhase quand une session de targeting est active.
    /// Local : avance le curseur ou passe au joueur suivant.
    /// Réseau : avance le curseur puis soumet au serveur quand tout est confirmé.
    /// </summary>
    public static void ConfirmAndSubmit(Player player)
    {
        int playerIndex = GetIndex(player);
        // Debug.Log($"[Pipeline] ConfirmAndSubmit — joueur {playerIndex} ({player?.name}) | réseau={NetworkSessionData.IsNetworkSession}");

        if (_isComplete)
            return;

        if (!NetworkSessionData.IsNetworkSession)
        {
            if (_confirmedPlayers.Contains(playerIndex))
                return;

            if (playerIndex == _currentLocalPlayerIndex)
            {
                if (EffectSelectionController.Advance())
                    AdvanceLocalPlayer();
            }
            else
            {
                int selCount = EffectStack.GetSelectionEffectsFor(playerIndex).Count;
                if (selCount > 0)
                    return;

                _confirmedPlayers.Add(playerIndex);
                GlobalSettings.Instance?.RefreshEndPhaseButtons();
            }
        }
        else
        {
            if (EffectSelectionController.Advance())
            {
                EffectSelectionController.ClearHighlights();
                SubmitToServer(player);
                GlobalSettings.Instance?.RefreshEndPhaseButtons();
            }
        }
    }

    // ── Requêtes d'état ───────────────────────────────────────────────────────

    public static bool BlocksEndPhaseButton(Player player)
    {
        int idx = GetIndex(player);

        bool result;
        if (!NetworkSessionData.IsNetworkSession)
        {
            if (_confirmedPlayers.Contains(idx))
                result = true;
            else if (idx == _currentLocalPlayerIndex)
                result = EffectSelectionController.HasPendingSelection;
            else
                result = EffectStack.GetSelectionEffectsFor(idx).Count > 0;
        }
        else
        {
            if (_hasSubmitted && !_isComplete)
                result = true;
            else
                result = EffectSelectionController.HasPendingSelection;
        }

        return result;
    }

    public static bool IsTargetingActiveForPlayer(Player player)
    {
        if (_isComplete)
            return false;

        return !NetworkSessionData.IsNetworkSession
            ? GetIndex(player) == _currentLocalPlayerIndex
            : player.MainPArea.AllowedToControlThisPlayer && !_hasSubmitted;
    }

    public static bool AreAllTargetsAssigned(Player player)
    {
        if (_isComplete)
            return false;

        if (!NetworkSessionData.IsNetworkSession)
            return GetIndex(player) == _currentLocalPlayerIndex && EffectSelectionController.IsAllSelected;

        return EffectSelectionController.IsAllSelected && !_hasSubmitted;
    }

    public static bool IsPlayerTargetingComplete(Player player)
    {
        if (_isComplete)
            return true;

        int idx = GetIndex(player);
        bool result;

        if (!NetworkSessionData.IsNetworkSession)
        {
            if (_confirmedPlayers.Contains(idx))
                result = true;
            else if (EffectStack.GetSelectionEffectsFor(idx).Count == 0)
                result = true;
            else if (idx == _currentLocalPlayerIndex)
                result = !EffectSelectionController.HasPendingSelection;
            else
                result = false;
        }
        else
        {
            result = player.MainPArea.AllowedToControlThisPlayer ? _hasSubmitted : _isComplete;
        }

        return result;
    }

    // ── Mode local : traitement séquentiel des joueurs ────────────────────────

    private static void LoadLocalPlayerSelections(int playerIndex)
    {
        _currentLocalPlayerIndex = playerIndex;

        List<PendingEffectSelection> selectionEffects = EffectStack.GetSelectionEffectsFor(playerIndex);
        // Debug.Log($"[Pipeline] LoadLocalPlayerSelections — joueur {playerIndex} | {selectionEffects.Count} sélection(s)");

        EffectSelectionController.LoadQueue(selectionEffects);

        if (!EffectSelectionController.HasPendingSelection)
            AdvanceLocalPlayer();

        GlobalSettings.Instance?.RefreshEndPhaseButtons();
    }

    private static void AdvanceLocalPlayer()
    {
        if (_localPlayerQueue.Count > 0)
        {
            _confirmedPlayers.Add(_currentLocalPlayerIndex);
            _localPlayerQueue.RemoveAt(0);
        }

        while (_localPlayerQueue.Count > 0 && _confirmedPlayers.Contains(_localPlayerQueue[0]))
            _localPlayerQueue.RemoveAt(0);

        if (_localPlayerQueue.Count > 0)
        {
            // Debug.Log($"[Pipeline] AdvanceLocalPlayer — joueur {_currentLocalPlayerIndex} confirmé → passage joueur {_localPlayerQueue[0]}");
            LoadLocalPlayerSelections(_localPlayerQueue[0]);
        }
        else
        {
            _currentLocalPlayerIndex = -1;
            List<PendingEffectSelection> allEffects = EffectStack.GetAllEffects();
            // Debug.Log($"[Pipeline] AdvanceLocalPlayer — tous joueurs traités → {(allEffects.Count == 0 ? "complétion immédiate" : $"ExecuteAll ({allEffects.Count} effet(s))")}");
            GlobalSettings.Instance?.RefreshEndPhaseButtons();
            // Pas d'effets : on complète immédiatement sans passer par la coroutine,
            // pour éviter une race condition si le joueur clique avant la fin du délai.
            if (allEffects.Count == 0)
                OnAllEffectsComplete();
            else
                ExecuteAll(allEffects);
        }
    }

    // ── Exécution (local et réseau) ───────────────────────────────────────────

    private static void ExecuteAll(List<PendingEffectSelection> effects)
    {
        // Debug.Log($"[Pipeline] ExecuteAll — {effects.Count} effet(s) à exécuter");
        EffectSelectionController.ClearHighlights();
        TargetingVisualEvents.RaiseEffectsExecuting();

        List<Action> callbacks  = new List<Action>();
        List<bool>   hasVisuals = new List<bool>();

        foreach (PendingEffectSelection e in effects)
        {
            PendingEffectSelection captured = e;
            callbacks.Add(() =>
            {
                captured.Context.SelectedTarget = captured.SelectedTarget;
                EffectRegistry.Execute(captured.Data, captured.Context);
            });
            hasVisuals.Add(!e.Data.RequiresPlayerInput || e.EligibleTargets.Count > 0);
        }

        TargetingVisualEvents.RaiseEffectsBatchPending(effects, hasVisuals);
        RunEffectsSequentially(callbacks, hasVisuals, OnAllEffectsComplete);
    }

    // ── Réseau : soumission et résolution canonique ───────────────────────────

    private static void SubmitToServer(Player player)
    {
        if (_hasSubmitted)
        {
            // Debug.Log("[Pipeline] SubmitToServer — SKIP: déjà soumis");
            return;
        }

        _hasSubmitted = true;

        List<PendingEffectSelection> allEffects = EffectStack.GetAllEffects();
        // Debug.Log($"[Pipeline] SubmitToServer — joueur {GetIndex(player)} | {allEffects.Count} effet(s) soumis au serveur");
        // foreach (PendingEffectSelection e in allEffects)
        //     Debug.Log($"[Pipeline] SubmitToServer   → {e.Data.EffectName} | trigger: {e.Data.Trigger} | srcID: {e.SourceEntityID} | effectIdx: {e.EffectIndexInCard}");
        int n = allEffects.Count;

        int[] sourceEntityIDs   = new int[n];
        int[] effectIndexes     = new int[n];
        int[] selectedTargetIDs = new int[n];

        for (int i = 0; i < n; i++)
        {
            sourceEntityIDs[i]   = allEffects[i].SourceEntityID;
            effectIndexes[i]     = allEffects[i].EffectIndexInCard;
            selectedTargetIDs[i] = allEffects[i].SelectedTarget?.ID ?? -1;
        }

        GameNetworkManager.Instance.SubmitEffectTargetsServerRpc(
            GetIndex(player), TurnManager.Instance.CurrentPhase,
            sourceEntityIDs, effectIndexes, selectedTargetIDs);
    }

    /// <summary>Appelé par GameNetworkManager sur tous les clients avec la résolution canonique du serveur.</summary>
    public static void ApplyCanonicalResolution(
        int[] sourceEntityIDs, int[] effectIndexes, int[] selectedTargetIDs, int effectSeed,
        bool isLocalPlayer = true)
    {
        System.Random masterRng = new System.Random(effectSeed);
        int[] subSeeds = new int[sourceEntityIDs.Length];
        for (int i = 0; i < subSeeds.Length; i++)
            subSeeds[i] = masterRng.Next();

        if (!isLocalPlayer)
        {
            // Mise à jour de l'état de jeu serveur uniquement : pas de visuels, pas de _isComplete.
            for (int i = 0; i < sourceEntityIDs.Length; i++)
            {
                CardEffectData data = ResolveEffectData(sourceEntityIDs[i], effectIndexes[i], out EffectContext ctx);
                if (data == null || ctx == null) continue;
                ctx.SelectedTarget = ResolveEntityByID(selectedTargetIDs[i]);
                EffectSO.SetNetworkRng(new System.Random(subSeeds[i]));
                EffectRegistry.Execute(data, ctx);
                EffectSO.ClearNetworkRng();
            }
            return;
        }

        TargetingVisualEvents.RaiseEffectsExecuting();

        // En réseau, les effets adverses n'ont pas de délai visuel en Regroup/Command/End
        // (évite la fuite d'information sur les cartes ou capacités adverses).
        // En BeginCombat, la phase est commune : les deux joueurs ont engagé leurs unités,
        // tous les effets déclenchés (Snipe, OnAttack…) sont visibles des deux côtés.
        Player localPlayer = NetworkSessionData.IsNetworkSession ? GlobalSettings.Instance?.localPlayer : null;
        bool isCombat = TurnManager.Instance != null && (
            TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.BeginCombat
            || TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Battle
            || TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.EndBattle
        );

        List<PendingEffectSelection> visualEffects = new List<PendingEffectSelection>();
        List<Action> callbacks  = new List<Action>();
        List<bool>   hasVisuals = new List<bool>();

        for (int i = 0; i < sourceEntityIDs.Length; i++)
        {
            CardEffectData data = ResolveEffectData(sourceEntityIDs[i], effectIndexes[i], out EffectContext ctx);
            // Debug.Log($"[Pipeline] Effect : {data.EffectName} by {ctx.Source.DisplayName}");

            if (data == null || ctx == null)
                continue;

            ctx.SelectedTarget = ResolveEntityByID(selectedTargetIDs[i]);

            visualEffects.Add(new PendingEffectSelection
            {
                Data            = data,
                Context         = ctx,
                SourceEntityID  = sourceEntityIDs[i],
                EligibleTargets = new List<IIdentifiable>()
            });

            bool isLocalEffect = isCombat || localPlayer == null || ctx.Caster == null || ctx.Caster == localPlayer;
            hasVisuals.Add(isLocalEffect && (!data.RequiresPlayerInput || selectedTargetIDs[i] >= 0));

            CardEffectData capturedData = data;
            EffectContext  capturedCtx  = ctx;
            int            seedIdx      = i;
            callbacks.Add(() =>
            {
                EffectSO.SetNetworkRng(new System.Random(subSeeds[seedIdx]));
                EffectRegistry.Execute(capturedData, capturedCtx);
                EffectSO.ClearNetworkRng();
            });
        }

        TargetingVisualEvents.RaiseEffectsBatchPending(visualEffects, hasVisuals);
        RunEffectsSequentially(callbacks, hasVisuals, OnAllEffectsComplete);
    }

    // ── Séquenceur visuel ─────────────────────────────────────────────────────

    private static void RunEffectsSequentially(List<Action> callbacks, List<bool> hasVisuals, Action onComplete)
    {
        int gen = _generation;
        if (TurnManager.Instance != null)
            TurnManager.Instance.StartCoroutine(EffectSequenceCoroutine(callbacks, hasVisuals, onComplete, gen));
        else
        {
            foreach (Action cb in callbacks)
                cb?.Invoke();
            onComplete?.Invoke();
        }
    }

    private static IEnumerator EffectSequenceCoroutine(List<Action> callbacks, List<bool> hasVisuals, Action onComplete, int generation)
    {
        float stackDelay = CardPreviewUI.Instance != null ? CardPreviewUI.Instance.StackAppearDelay : 0.5f;
        yield return new UnityEngine.WaitForSeconds(stackDelay);

        for (int i = 0; i < callbacks.Count; i++)
        {
            bool showVisual = hasVisuals != null && i < hasVisuals.Count && hasVisuals[i];
            if (showVisual)
            {
                float delay = TurnManager.Instance != null ? TurnManager.Instance.EffectSequenceDelay : 1.5f;
                yield return new UnityEngine.WaitForSeconds(delay);
            }
            callbacks[i]?.Invoke();
            if (showVisual)
                TargetingVisualEvents.RaiseEffectResolved();
        }

        TargetingVisualEvents.RaiseEffectsBatchComplete();

        // N'appelle onComplete que si cette coroutine appartient encore à la phase courante.
        // ResetForNewPhase incrémente _generation, ce qui invalide les coroutines périmées.
        if (_generation == generation)
            onComplete?.Invoke();
    }

    private static void OnAllEffectsComplete()
    {
        _isComplete = true;
        // Debug.Log("[Pipeline] OnAllEffectsComplete — _isComplete = true");
        TurnManager.RefreshAllPlayableHighlights();
        GlobalSettings.Instance?.RefreshEndPhaseButtons();
    }

    // ── Résolution d'IDs (réseau) ─────────────────────────────────────────────

    private static CardEffectData ResolveEffectData(int sourceID, int effectIdx, out EffectContext ctx)
    {
        ctx = null;

        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(sourceID, out CreatureLogic creature))
        {
            bool indexIsValid = creature.ca.Effects != null
                && effectIdx >= 0 && effectIdx < creature.ca.Effects.Count;

            if (!indexIsValid)
                return null;

            ctx = new EffectContext { Caster = creature.owner, Source = creature };
            return creature.ca.Effects[effectIdx];
        }

        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(sourceID, out BuildingLogic building))
        {
            bool indexIsValid = building.ca.Effects != null
                && effectIdx >= 0 && effectIdx < building.ca.Effects.Count;

            if (!indexIsValid)
                return null;

            ctx = new EffectContext { Caster = building.owner, Source = building };
            return building.ca.Effects[effectIdx];
        }

        return null;
    }

    private static IIdentifiable ResolveEntityByID(int id)
    {
        if (id < 0)
            return null;

        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic c))
            return c;

        if (BuildingLogic.BuildingsCreatedThisGame.TryGetValue(id, out BuildingLogic b))
            return b;

        if (BaseLogic.BasesCreatedThisGame.TryGetValue(id, out BaseLogic pb))
            return pb;

        foreach (ZoneManager z in ZoneManager.AllZones)
        {
            if (z.Logic.ID == id)
                return z.Logic;
        }

        foreach (Player p in Player.Players)
        {
            if (p.ID == id)
                return p;
        }

        return null;
    }

    private static int GetIndex(Player p)
        => p == null || Player.Players == null ? -1 : System.Array.IndexOf(Player.Players, p);
}
