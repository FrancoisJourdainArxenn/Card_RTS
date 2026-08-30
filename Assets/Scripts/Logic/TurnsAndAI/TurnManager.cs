using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Orchestrates simultaneous play: shared phases (Regroup, Command, Battle, End).
/// Phase advances only after every participant registers end phase (scalable N players).
/// </summary>
public class TurnManager : MonoBehaviour
{
    public static event System.Action OnRoundStart;
    public static event System.Action OnPhaseEntered;


    public int initdraw;
    [SerializeField] private float effectSequenceDelay = 1f;
    [SerializeField] private float combatSequenceDelay = .5f;
    public float EffectSequenceDelay => effectSequenceDelay;
    public float CombatSequenceDelay => combatSequenceDelay;

    public static TurnManager Instance;

    public enum TurnPhases { Regroup, Command, BeginCombat, Battle, EndBattle, EndTurn }

    public TMP_Text phaseText;

    // private RopeTimer timer;
    private TurnPhases currentPhase = TurnPhases.Command;
    private int currentRound = 1;
    private bool[] phaseReady;
    private readonly List<(int creatureUniqueID, int targetBaseID, int tablePos)> _soloMoveBuffer = new();
    // Embarquements en attente, résolus AVANT _soloMoveBuffer (voir FlushSoloBoardBuffer) pour qu'un
    // embarquement et le départ de son transporteur ordonnés le même tour se résolvent ensemble.
    private readonly List<(int passengerUniqueID, int transportUniqueID)> _soloBoardBuffer = new();
    // Issue du round de combat solo en cours, calculée dans DelayedBattleStart juste avant l'enqueue
    // ordonné, consommée par AutoAdvanceFromBattleAfterCombat pour savoir si la transition normale
    // vers EndBattle doit être sautée (partie déjà terminée via GameOverCommand). Miroir solo de
    // GameNetworkManager._pendingRoundOutcome.
    private ZoneCombatResolver.RoundOutcome? _lastRoundOutcome;

    public TurnPhases CurrentPhase => currentPhase;
    public int CurrentRound => currentRound;

    public int ParticipantCount => Player.Players != null ? Player.Players.Length : 0;

    void Awake()
    {
        Instance = this;
        UpdatePhaseText();
        // timer = GetComponent<RopeTimer>();

    }

    void Start()
    {
        initdraw = GlobalSettings.Instance.initdraw;

        //GameStart local
        if (!NetworkSessionData.IsNetworkSession)
        {
            OnGameStart();
        }
    }

    public void OnGameStart(int? seed = null, int[] cardInHandIDs = null, int deckIdxLow = -1, int deckIdxTop = -1, int[] heroCardIDs = null)
    {
        EffectRegistry.Reset();
        // Sans ça, une attaque interrompue en plein vol pendant une partie précédente (même session
        // Play, sans reload de scène complet — voir SceneReloader.ReloadScene) laisse le compteur
        // bloqué > 0 et gèle tout repositionnement de table pour la nouvelle partie (voir
        // CreatureAttackVisual.ResetFlightCounter).
        CreatureAttackVisual.ResetFlightCounter();
        if (Player.Players == null || Player.Players.Length < 2)
        {
            // Debug.LogError("TurnManager: need at least 2 Player instances.");
            return;
        }

        if (NetworkSessionData.IsNetworkSession)
        {
            foreach (Player p in Player.Players)
            {
                bool isLow = p == GlobalSettings.Instance.LowPlayer;
                DeckSO preset = GameNetworkManager.Instance.GetDeckPresetForPlayer(isLow ? deckIdxLow : deckIdxTop);
                if (preset != null)
                {
                    p.deck.LoadDeck(preset);
                    if (preset.sharedPool != null && preset.sharedPool.baseAsset != null)
                        p.ApplyBaseAssetOverride(preset.sharedPool.baseAsset);
                }
            }
        }

        foreach (Player p in Player.Players)
        {
            p.LoadCharacterInfoFromAsset();
            p.TransmitInfoAboutPlayerToVisual();
            p.matchStats.Reset();
        }

        if (seed.HasValue)
        {
            for (int idx = 0; idx < Player.Players.Length; idx++)
            {
                Player p = Player.Players[idx];
                p.deck.ShuffleWithSeed(seed.Value + idx);
                p.deck.ResetTimesDrawn();
                // if (p.deck.cards.Count >= 2)
                //     Debug.Log($"[DeckCheck] Player {idx} top1={p.deck.cards[0].name}, top2={p.deck.cards[1].name}");
            }
            // Debug.Log($"TurnManager: Deck shuffled with seed {seed.Value}");

        }
        else //Shuffle Local
        {
            for (int idx = 0; idx < Player.Players.Length; idx++)
            {
                Player p = Player.Players[idx];
                p.deck.Shuffle();
                p.deck.ResetTimesDrawn();
            }
            // Debug.Log("TurnManager: Deck shuffled with random seed");
        }

        CardLogic.CardsCreatedThisGame.Clear();
        CreatureLogic.CreaturesCreatedThisGame.Clear();
        BuildingLogic.BuildingsCreatedThisGame.Clear();

        EnsurePhaseReadyMatchesPlayers();
        ResetPhaseReadyFlags();

        for (int idx = 0; idx < Player.Players.Length; idx++)
        {
            Player p = Player.Players[idx];
            if (p.deck.playerDeck.heroCard != null)
            {
                int heroID = (NetworkSessionData.IsNetworkSession && heroCardIDs != null) ? heroCardIDs[idx] : -1;
                p.GetACardNotFromDeck(p.deck.playerDeck.heroCard, heroID);
            }
        }
        
        int drawSeedOffset = 0;
        int deckSeed = seed ?? 0;
        for (int i = 0; i < initdraw; i++)
        {
            for (int j = 0; j < Player.Players.Length; j++)
            {
                Player p = Player.Players[j];
                int cardInHandID = cardInHandIDs == null ? -1 : cardInHandIDs[j * initdraw + i];
                p.DrawACard(true, cardInHandID, deckSeed + drawSeedOffset++);
            }
        }

        if (NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer)
            GameNetworkManager.Instance.InitDrawSeedOffset(drawSeedOffset);
        foreach (Player p in Player.Players)
            p.OnTurnStart();

        EnterPhase(TurnPhases.Command);
        StartCoroutine(HighlightAfterDraws());

    }

    IEnumerator HighlightAfterDraws()
    {
        yield return new WaitWhile(() => Command.CardDrawPending());
        RefreshAllPlayableHighlights();
    }

    public void OnRopeTimerExpired()
    {
        if (Player.Players == null)
            return;
        for (int i = 0; i < Player.Players.Length; i++)
            RegisterEndPhase(i);
    }

    // public void StopTheTimer()
    // {
    //     if (timer != null)
    //         timer.StopTimer();
    // }

    public bool HasPlayerRegisteredEndPhase(int participantIndex)
    {
        if (phaseReady == null || participantIndex < 0 || participantIndex >= phaseReady.Length)
            return false;
        return phaseReady[participantIndex];
    }

    public bool HasPlayerRegisteredEndPhase(Player player)
    {
        int i = GetParticipantIndex(player);
        if (i < 0)
            return false;
        return HasPlayerRegisteredEndPhase(i);
    }

    /// <summary>Registers end-of-phase for the participant at this index in <see cref="Player.Players"/>.</summary>
    public void RegisterEndPhase(int participantIndex)
    {
        Player player = (Player.Players != null && participantIndex >= 0 && participantIndex < Player.Players.Length)
            ? Player.Players[participantIndex] : null;

        // Any phase with an active targeting session AND the player hasn't already submitted:
        // clicking End Phase = confirming selections.
        // BeginCombat always goes through this path (confirmation IS the phase-end).
        // En réseau seulement : si le joueur a déjà soumis ses sélections (auto ou manuel),
        // on bypass ConfirmAndSubmit pour envoyer directement RegisterEndPhaseServerRpc.
        // En local ce check n'est pas nécessaire — le state machine gère déjà tout via AdvanceLocalPlayer.
        bool playerTargetingDone = NetworkSessionData.IsNetworkSession &&
            (player == null || PhaseEffectPipeline.IsPlayerTargetingComplete(player));
        bool routeToConfirm = currentPhase == TurnPhases.BeginCombat || (!PhaseEffectPipeline.IsComplete && !playerTargetingDone);

        // Debug.Log($"[TurnMgr] RegisterEndPhase — idx={participantIndex} | phase={currentPhase} | IsComplete={PhaseEffectPipeline.IsComplete} | playerTargetingDone={playerTargetingDone} | réseau={NetworkSessionData.IsNetworkSession} | → {(routeToConfirm ? "ConfirmAndSubmit" : "MarkReady")}");

        if (routeToConfirm)
        {
            if (NetworkSessionData.IsNetworkSession)
            {
                bool isMyPlayer = player != null && player.MainPArea.AllowedToControlThisPlayer;
                if (!isMyPlayer) return;
            }

            if (player != null)
                PhaseEffectPipeline.ConfirmAndSubmit(player);
            return;
        }

        if (NetworkSessionData.IsNetworkSession)
        {
            bool isMyPlayer = player != null && player.MainPArea.AllowedToControlThisPlayer;

            if (!isMyPlayer)
                return; // On ne traite jamais la phase-end d'un joueur qu'on ne contrôle pas

            if (currentPhase == TurnPhases.Battle)
            {
                // En Battle phase, on soumet l'attribution des dégâts au serveur.
                // Le serveur attend les deux soumissions, merge, puis déclenche la transition.
                // Ce chemin est identique pour le host (serveur) et le client pur,
                // car SubmitBattleAssignmentServerRpc est accessible aux deux via InvokePermission.Everyone.
                ZoneCombatResolver.BattleAssignment assignment =
                    ZoneCombatResolver.SerializeMyAttackAssignments(participantIndex);
                GameNetworkManager.Instance.SubmitBattleAssignmentServerRpc(
                    participantIndex,
                    assignment.CreatureIDs,     assignment.CreatureDamages,
                    assignment.BaseIDs,         assignment.BaseDamages,
                    assignment.TargetPlayerIDs, assignment.PlayerDamages,
                    assignment.BuildingIDs,     assignment.BuildingDamages);
                return;
            }

            // Pour les autres phases, le client envoie un ServerRpc standard.
            // Le host (serveur) tombe dans le bloc local ci-dessous.
            if (!Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                GameNetworkManager.Instance.RegisterEndPhaseServerRpc(participantIndex, currentPhase);
                return;
            }
        }

        EnsurePhaseReadyMatchesPlayers();
        if (phaseReady == null || participantIndex < 0 || participantIndex >= phaseReady.Length)
            return;
        if (phaseReady[participantIndex])
            return;

        phaseReady[participantIndex] = true;

        if (NetworkSessionData.IsNetworkSession)
        {
            // Diffuser l'état "prêt" à tous les clients pour griser le bon bouton
            GameNetworkManager.Instance.SyncPlayerReadyClientRpc(participantIndex, currentPhase);
        }
        else
        {
            if (GlobalSettings.Instance != null)
                GlobalSettings.Instance.RefreshEndPhaseButtons();
        }

        if (AllParticipantsRegisteredEndPhase())
            AdvancePhaseWhenAllReady();
    }

    /// <summary>
    /// Version serveur-only de RegisterEndPhase : marque directement un joueur comme prêt
    /// sans passer par la logique réseau. Appelé par GameNetworkManager après que les deux
    /// joueurs ont soumis leurs attributions de dégâts en Battle phase.
    /// </summary>
    public void ForceRegisterEndPhase(int participantIndex)
    {
        EnsurePhaseReadyMatchesPlayers();
        if (phaseReady == null || participantIndex < 0 || participantIndex >= phaseReady.Length)
            return;
        if (phaseReady[participantIndex])
            return;

        phaseReady[participantIndex] = true;
        GameNetworkManager.Instance.SyncPlayerReadyClientRpc(participantIndex, currentPhase);

        if (AllParticipantsRegisteredEndPhase())
            AdvancePhaseWhenAllReady();
    }

    /// <summary>Registers end-of-phase by player reference (resolves index in Player.Players).</summary>
    public void RegisterEndPhase(Player player)
    {
        int i = GetParticipantIndex(player);
        if (i < 0)
            return;
        RegisterEndPhase(i);
    }

    void AdvancePhaseWhenAllReady()
    {
        // if (timer != null)
        //     timer.StopTimer();

        bool roundEnded = currentPhase == TurnPhases.Battle;

        TurnPhases next = currentPhase switch
        {
            TurnPhases.Command     => TurnPhases.BeginCombat,
            TurnPhases.BeginCombat => TurnPhases.Battle,
            TurnPhases.Battle      => TurnPhases.EndBattle,
            TurnPhases.EndBattle   => TurnPhases.EndTurn,
            TurnPhases.EndTurn     => TurnPhases.Regroup,
            TurnPhases.Regroup     => TurnPhases.Command,
            _                      => TurnPhases.Command
        };


        int newRound = roundEnded ? currentRound + 1 : currentRound;
        // Debug.Log($"[TurnMgr] AdvancePhaseWhenAllReady — {currentPhase} → {next} (round {currentRound} → {newRound})");

        if (NetworkSessionData.IsNetworkSession)
        {
            // Le serveur diffuse la transition à tous les clients (y compris lui-même).
            // C'est PhaseTransitionClientRpc qui appellera EnterPhase et OnTurnEnd.
            GameNetworkManager.Instance.FlushBuffer();
            GameNetworkManager.Instance.BroadcastPhaseTransition(next, roundEnded, newRound);
        }
        else
        {
            if (currentPhase == TurnPhases.Command)
            {
                FlushSoloBoardBuffer();
                FlushSoloMoveBuffer();
            }
            bool isCombatPhase = currentPhase == TurnPhases.BeginCombat ||
                                 currentPhase == TurnPhases.Battle      ||
                                 currentPhase == TurnPhases.EndBattle;
            if (isCombatPhase)
            {
                StartCoroutine(CombatPhaseTransitionCoroutine(next, roundEnded));
            }
            else
            {
                if (roundEnded)
                {
                    foreach (Player p in Player.Players)
                        p.GetComponent<TurnMaker>().OnTurnEnd();
                    currentRound++;
                }
                EnterPhase(next);
            }
        }
    }

    bool AnyZoneHasPossibleCombat()
    {
        foreach (ZoneCombatResolver r in ZoneCombatResolver.AllResolvers)
            if (r.HasPossibleCombat()) return true;
        return false;
    }

    /// <summary>Appelé par PhaseTransitionClientRpc pour mettre à jour le numéro de tour.</summary>
    public void SetCurrentRound(int round)
    {
        currentRound = round;
    }

    /// <summary>
    /// Appelé par SyncPlayerReadyClientRpc pour mettre à jour phaseReady localement
    /// et rafraîchir l'état des boutons (grisage du bouton du joueur qui a cliqué).
    /// </summary>
    public void SetPlayerReady(int playerIndex)
    {
        EnsurePhaseReadyMatchesPlayers();
        if (phaseReady != null && playerIndex >= 0 && playerIndex < phaseReady.Length)
            phaseReady[playerIndex] = true;
        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
    }

    public void EnterPhase(TurnPhases phase)
    {
        bool isServer = NetworkSessionData.IsNetworkSession && Unity.Netcode.NetworkManager.Singleton.IsServer;
        // Debug.Log($"[EnterPhase] {currentPhase} → {phase} | round={currentRound} | role={(isServer ? "SERVER" : "CLIENT")} | frame={Time.frameCount}");
        // Debug.Log($"[TurnMgr] EnterPhase → {phase} (depuis {currentPhase}, round {currentRound})");
        currentPhase = phase;
        OnPhaseEntered?.Invoke();
        EnsurePhaseReadyMatchesPlayers();
        ResetPhaseReadyFlags();
        PhaseEffectPipeline.ResetForNewPhase();

        UpdatePhaseText();

        // if (timer != null)
        // {
        //     timer.StopTimer();
        //     bool timerPhase = phase == TurnPhases.Command || phase == TurnPhases.Battle;
        //     if (timerPhase)
        //         timer.StartTimer();
        // }


        switch (phase)
        {
            case TurnPhases.Regroup:
                // new ShowMessageCommand("Regroup", 1.5f).AddToQueue();
                OnRoundStart?.Invoke(); 
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnRegroupPhaseStart();
                StartCoroutine(AutoAdvanceFromRegroup());
                break;
            case TurnPhases.Command:
                // new ShowMessageCommand("Command", 1.5f).AddToQueue();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnCommandPhaseEntered();
                break;
            case TurnPhases.BeginCombat:
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnBeginCombatPhaseEntered();
                StartCoroutine(AutoAdvanceFromBeginCombat());
                break;
            case TurnPhases.Battle:
                // new ShowMessageCommand("Battle", 1.5f).AddToQueue();
                if (!AnyZoneHasPossibleCombat())
                {
                    StartCoroutine(AutoAdvanceFromBattle());
                    break;
                }
                CameraController.Instance?.EnterBattleCam();
                StartCoroutine(DelayedBattleStart());
                break;
            case TurnPhases.EndBattle:
                CameraController.Instance?.ExitBattleCam();
                foreach (ZoneCombatResolver r in ZoneCombatResolver.AllResolvers)
                    r.OnBattlePhaseEnd();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnEndBattlePhaseEntered();
                StartCoroutine(AutoAdvanceFromEndBattle());
                break;
            case TurnPhases.EndTurn:
                // new ShowMessageCommand("End", 1.5f).AddToQueue();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnEndPhaseEntered();
                StartCoroutine(AutoAdvanceFromEnd());
                break;
        }

        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
        RefreshAllPlayableHighlights();
        PhaseEffectPipeline.BeginLocalSelectionSession();
    }

    void UpdatePhaseText()
    {
        if (phaseText == null)
            return;
        string label = currentPhase switch
        {
            TurnPhases.Regroup     => "Regroup",
            TurnPhases.Command     => "Command",
            TurnPhases.BeginCombat => "Begin Combat",
            TurnPhases.Battle      => "Battle",
            TurnPhases.EndBattle   => "End Battle",
            TurnPhases.EndTurn     => "End Turn",
            _ => ""
        };
        phaseText.text = label;
    }
    IEnumerator DelayedBattleStart()
    {
        yield return new WaitForSeconds(combatSequenceDelay);
        Debug.Log($"[Battle] DelayedBattleStart — {ZoneCombatResolver.AllResolvers.Count} resolver(s) à traiter");
        int idx = 0;
        foreach (ZoneCombatResolver r in ZoneCombatResolver.AllResolvers)
        {
            Debug.Log($"[Battle] OnBattlePhaseStart resolver #{idx} ({r.name})");
            try
            {
                r.OnBattlePhaseStart();
            }
            catch (System.Exception e)
            {
                // Sans ce try/catch, une exception ici arrête la coroutine : tous les resolvers suivants
                // dans cette liste (donc tous leurs combats) ne sont jamais traités, et AutoSubmitBattleAssignment
                // (réseau) n'est jamais appelé non plus — ce qui bloque la partie pour les deux joueurs.
                Debug.LogError($"[Battle] EXCEPTION dans OnBattlePhaseStart du resolver #{idx} ({r.name}) — les resolvers suivants auraient été SAUTÉS sans ce filet: {e}");
            }
            idx++;
        }
        Debug.Log($"[Battle] Tous les resolvers traités (planification) — file de commandes: {Command.CommandQueue.Count} en attente, playingQueue={Command.playingQueue}");
        if (!NetworkSessionData.IsNetworkSession)
        {
            // Planification de TOUS les resolvers terminée avant tout enqueue (voir
            // ZoneCombatResolver.OnBattlePhaseStart) — le round est donc déjà connu ici, avant la
            // moindre animation, exactement comme côté réseau (SubmitBattleAssignmentServerRpc).
            ZoneCombatResolver.RoundOutcome outcome = ZoneCombatResolver.ComputeRoundOutcome();
            _lastRoundOutcome = outcome;
            ZoneCombatResolver.EnqueueAllPlannedBattleCommandsSolo(outcome);
            StartCoroutine(AutoAdvanceFromBattleAfterCombat());
        }
        else
            AutoSubmitBattleAssignment();
    }

    public static void RefreshAllPlayableHighlights()
    {
        if (Command.CardDrawPending())
            return;
        if (Player.Players == null)
            return;
        if (NetworkSessionData.IsNetworkSession)
        {
            Player local = GlobalSettings.Instance.localPlayer;
            if (PhaseEffectPipeline.IsPlayerTargetingComplete(local))
                local.HighlightPlayableCards();
        }
        else
        {
            foreach (Player p in Player.Players)
                if (PhaseEffectPipeline.IsPlayerTargetingComplete(p))
                    p.HighlightPlayableCards();
        }
    }

    public bool MayPlayerUseControlsInPhase(Player player)
    {
        if (player == null || !player.MainPArea.ControlsON)
            return false;
        if (Command.CardDrawPending())
            return false;
        return currentPhase == TurnPhases.Command || currentPhase == TurnPhases.Battle;
    }

    public bool IsCommandPhase => currentPhase == TurnPhases.Command;
    public bool IsBattlePhase => currentPhase == TurnPhases.Battle;

    void EnsurePhaseReadyMatchesPlayers()
    {
        if (Player.Players == null)
            return;
        int n = Player.Players.Length;
        if (phaseReady == null || phaseReady.Length != n)
            phaseReady = new bool[n];
    }

    void ResetPhaseReadyFlags()
    {
        if (phaseReady == null)
            return;
        for (int i = 0; i < phaseReady.Length; i++)
            phaseReady[i] = false;
    }

    bool AllParticipantsRegisteredEndPhase()
    {
        if (phaseReady == null)
            return false;
        for (int i = 0; i < phaseReady.Length; i++)
        {
            if (!phaseReady[i])
                return false;
        }
        return true;
    }

    static int GetParticipantIndex(Player p)
    {
        if (p == null || Player.Players == null)
            return -1;
        return System.Array.IndexOf(Player.Players, p);
    }

    IEnumerator AutoAdvanceFromRegroup()
    {
        float t0 = Time.realtimeSinceStartup;
        // Debug.Log($"[Regroup] Entrée WaitWhile — round={currentRound} IsServer={(NetworkSessionData.IsNetworkSession && Unity.Netcode.NetworkManager.Singleton.IsServer)}");
        yield return new WaitWhile(() =>
        {
            bool stuck = !PhaseEffectPipeline.IsComplete || Command.playingQueue || Command.CardDrawPending();
            if (stuck && Time.realtimeSinceStartup - t0 > 5f && Time.frameCount % 60 == 0)
                Debug.LogWarning($"[Regroup] TOUJOURS bloqué après {Time.realtimeSinceStartup - t0:F1}s — IsComplete={PhaseEffectPipeline.IsComplete} playingQueue={Command.playingQueue} CardDrawPending={Command.CardDrawPending()}");
            return stuck;
        });
        // Debug.Log($"[Regroup] WaitWhile résolu après {Time.realtimeSinceStartup - t0:F1}s → EnterPhase(Command)");
        // yield return new WaitForSeconds(1.5f);
        if (currentPhase == TurnPhases.Regroup)
            EnterPhase(TurnPhases.Command);
    }
    
    public IEnumerator DrainPendingDeaths()
    {
        yield return new WaitWhile(() => Command.playingQueue);
        while (CreatureLogic.PendingDeathList.Count > 0)
        {
            CreatureLogic.ProcessPendingDeaths();     // déclenche les deathrattles
            yield return new WaitWhile(() => Command.playingQueue);   // attend la fin de la chaîne
        }
        // ici : queue vide ET aucune mort en attente
    }
    IEnumerator AutoAdvanceFromBeginCombat()
    {
        yield return new WaitWhile(() => !PhaseEffectPipeline.IsComplete || Command.playingQueue);
        if (!NetworkSessionData.IsNetworkSession || Unity.Netcode.NetworkManager.Singleton.IsServer)
            AdvancePhaseWhenAllReady();
    }

    IEnumerator AutoAdvanceFromBattle()
    {
        yield return new WaitForSeconds(1.5f);
        if (!NetworkSessionData.IsNetworkSession || Unity.Netcode.NetworkManager.Singleton.IsServer)
            AdvancePhaseWhenAllReady();
    }

    IEnumerator AutoAdvanceFromBattleAfterCombat()
    {
        yield return null; // one frame so the queue can start
        float t0 = Time.realtimeSinceStartup;
        Debug.Log($"[Battle] AutoAdvanceFromBattleAfterCombat — attente fin de file (playingQueue={Command.playingQueue}, restants={Command.CommandQueue.Count})");
        yield return new WaitWhile(() =>
        {
            bool stuck = Command.playingQueue;
            if (stuck && Time.realtimeSinceStartup - t0 > 5f && Time.frameCount % 60 == 0)
                Debug.LogWarning($"[Battle] TOUJOURS bloqué après {Time.realtimeSinceStartup - t0:F1}s — playingQueue={Command.playingQueue} restants={Command.CommandQueue.Count} — la file de commandes de combat est probablement gelée");
            return stuck;
        });
        bool wasDecisive = _lastRoundOutcome?.Decisive ?? false;
        _lastRoundOutcome = null;
        if (wasDecisive)
        {
            // Round décisif — GameOverCommand a déjà tourné (voir ZoneCombatResolver.
            // EnqueueOrderedBattleCommands). Pas de transition vers EndBattle : currentPhase reste
            // figé sur Battle, les contrôles sont déjà désactivés.
            Debug.Log("[Battle] Round décisif — transition vers EndBattle sautée (partie terminée)");
            yield break;
        }

        Debug.Log($"[Battle] File vidée après {Time.realtimeSinceStartup - t0:F1}s → passage à EndBattle");
        if (currentPhase == TurnPhases.Battle)
            AdvancePhaseWhenAllReady();
    }

    void AutoSubmitBattleAssignment()
    {
        int localIndex = System.Array.IndexOf(Player.Players, GlobalSettings.Instance.localPlayer);
        if (localIndex < 0)
        {
            Debug.LogWarning("[Battle][Client] AutoSubmitBattleAssignment — localIndex introuvable, soumission ANNULÉE (ce joueur ne soumettra jamais son assignment — le serveur restera bloqué à attendre)");
            return;
        }
        ZoneCombatResolver.BattleAssignment assignment =
            ZoneCombatResolver.SerializeMyAttackAssignments(localIndex);
        Debug.Log($"[Battle][Client] AutoSubmitBattleAssignment — joueur {localIndex} | créatures={assignment.CreatureIDs.Length} bases={assignment.BaseIDs.Length} joueurs={assignment.TargetPlayerIDs.Length} bâtiments={assignment.BuildingIDs.Length}");
        GameNetworkManager.Instance.SubmitBattleAssignmentServerRpc(
            localIndex,
            assignment.CreatureIDs,     assignment.CreatureDamages,
            assignment.BaseIDs,         assignment.BaseDamages,
            assignment.TargetPlayerIDs, assignment.PlayerDamages,
            assignment.BuildingIDs,     assignment.BuildingDamages);
    }

    IEnumerator AutoAdvanceFromEndBattle()
    {
        // Client passif : attendre BroadcastDeathDrainClientRpc qui gère tout.
        if (NetworkSessionData.IsNetworkSession && !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            Debug.Log("[AutoAdvanceFromEnd][Client] yield break — en attente de BroadcastDeathDrainClientRpc");
            yield break;
        }

        Debug.Log($"[AutoAdvanceFromEnd][Server] WaitWhile démarré — IsComplete={PhaseEffectPipeline.IsComplete}, playingQueue={Command.playingQueue}, PendingDeaths={CreatureLogic.PendingDeathList.Count}");
        yield return new WaitWhile(() => !PhaseEffectPipeline.IsComplete || Command.playingQueue);
        Debug.Log($"[AutoAdvanceFromEnd][Server] WaitWhile résolu — début drain, {CreatureLogic.PendingDeathList.Count} mort(s) en attente");

        if (NetworkSessionData.IsNetworkSession)
        {
            DeathDrainRecorder.Begin();
            while (CreatureLogic.PendingDeathList.Count > 0)
                CreatureLogic.ProcessPendingDeaths();
            List<DeathDrainRecorder.DrainEvent> events = DeathDrainRecorder.End();
            Debug.Log($"[AutoAdvanceFromEnd][Server] Drain terminé — {events.Count} événement(s), broadcast vers clients");

            CommandMoveTracker.CrossingDispatchResult crossingDispatch = CommandMoveTracker.ComputeCrossingDispatch();
            if (crossingDispatch.Relocations.Count > 0)
            {
                CommandMoveTracker.ApplyCrossingDispatch(crossingDispatch);
                GameNetworkManager.Instance.BroadcastCrossingDispatch(crossingDispatch);
            }

            GameNetworkManager.Instance.BroadcastDeathDrain(events, TurnPhases.EndTurn);
            // La transition vers EndTurn est déclenchée par BroadcastDeathDrainClientRpc sur tous les clients.
        }
        else
        {
            yield return StartCoroutine(DrainPendingDeaths());
            CommandMoveTracker.CrossingDispatchResult crossingDispatch = CommandMoveTracker.ComputeCrossingDispatch();
            CommandMoveTracker.ApplyCrossingDispatch(crossingDispatch);
            EnterPhase(TurnPhases.EndTurn);
        }
    }
    
    IEnumerator AutoAdvanceFromEnd()
    {
        yield return new WaitWhile(() => !PhaseEffectPipeline.IsComplete || Command.playingQueue);
        if (!NetworkSessionData.IsNetworkSession || Unity.Netcode.NetworkManager.Singleton.IsServer)
            AdvancePhaseWhenAllReady();
    }

    public void EnqueueSoloMove(int creatureUniqueID, int targetBaseID, int tablePos)
    {
        _soloMoveBuffer.Add((creatureUniqueID, targetBaseID, tablePos));
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureUniqueID, out CreatureLogic creature))
            creature.IsPendingMove = true;
    }

    public void CancelSoloMove(int creatureUniqueID)
    {
        _soloMoveBuffer.RemoveAll(m => m.creatureUniqueID == creatureUniqueID);
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureUniqueID, out CreatureLogic creature))
            creature.IsPendingMove = false;
    }

    public void EnqueueSoloBoard(int passengerUniqueID, int transportUniqueID)
    {
        Debug.Log($"[Transport] EnqueueSoloBoard — passenger={passengerUniqueID}, transport={transportUniqueID} (buffer now {_soloBoardBuffer.Count + 1})");
        _soloBoardBuffer.Add((passengerUniqueID, transportUniqueID));
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(passengerUniqueID, out CreatureLogic creature))
            creature.IsPendingMove = true;
    }

    public void CancelSoloBoard(int passengerUniqueID)
    {
        int removed = _soloBoardBuffer.RemoveAll(b => b.passengerUniqueID == passengerUniqueID);
        Debug.Log($"[Transport] CancelSoloBoard — passenger={passengerUniqueID}, removed={removed}");
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(passengerUniqueID, out CreatureLogic creature))
            creature.IsPendingMove = false;
    }

    private void FlushSoloBoardBuffer()
    {
        Debug.Log($"[Transport] FlushSoloBoardBuffer — resolving {_soloBoardBuffer.Count} board(s)");
        foreach (var (passengerID, transportID) in _soloBoardBuffer)
        {
            GameObject passengerGO = IDHolder.GetGameObjectWithID(passengerID);
            if (passengerGO != null && passengerGO.TryGetComponent(out OneCreatureManager ocm))
                ocm.ClearPendingMoveArrow();
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(passengerID, out CreatureLogic creature))
                creature.Board(transportID);
        }
        _soloBoardBuffer.Clear();
    }

    private void FlushSoloMoveBuffer()
    {
        List<CommandMoveTracker.PendingMoveInfo> pending = new();
        foreach (var (id, baseID, pos) in _soloMoveBuffer)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic creature))
                continue;
            pending.Add(new CommandMoveTracker.PendingMoveInfo
            {
                creatureID = id,
                originBaseID = creature.BaseID,
                targetBaseID = baseID,
                tablePos = pos,
                owner = creature.owner
            });
        }
        CommandMoveTracker.CrossingRedirectResult crossingResult = CommandMoveTracker.ResolveCrossings(pending);
        Dictionary<int, int> redirects = crossingResult.Redirects;

        foreach (var (id, baseID, pos) in _soloMoveBuffer)
        {
            GameObject creatureGO = IDHolder.GetGameObjectWithID(id);
            if (creatureGO != null && creatureGO.TryGetComponent(out OneCreatureManager ocm))
            {
                ocm.ClearPendingMoveArrow();
                ocm.DestroyPendingMoveGhost();
            }
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic creature))
            {
                int finalBaseID = redirects.TryGetValue(id, out int redirectedBaseID) ? redirectedBaseID : baseID;
                creature.Move(finalBaseID, pos);
            }
        }
        _soloMoveBuffer.Clear();
    }

    IEnumerator CombatPhaseTransitionCoroutine(TurnPhases next, bool roundEnded)
    {
        yield return StartCoroutine(DrainPendingDeaths());
        if (roundEnded)
        {
            foreach (Player p in Player.Players)
                p.GetComponent<TurnMaker>().OnTurnEnd();
            currentRound++;
        }
        EnterPhase(next);
    }

}
