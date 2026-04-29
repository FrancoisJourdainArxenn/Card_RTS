using System.Collections;
using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Orchestrates simultaneous play: shared phases (Regroup, Command, Battle, End).
/// Phase advances only after every participant registers end phase (scalable N players).
/// </summary>
public class TurnManager : MonoBehaviour
{
    public int initdraw = 5;

    public static TurnManager Instance;

    public enum TurnPhases { Regroup, Command, Battle, End }

    public TMP_Text phaseText;

    private RopeTimer timer;
    private TurnPhases currentPhase = TurnPhases.Command;
    private int currentRound = 1;
    private bool[] phaseReady;

    public TurnPhases CurrentPhase => currentPhase;
    public int CurrentRound => currentRound;

    public int ParticipantCount => Player.Players != null ? Player.Players.Length : 0;

    void Awake()
    {
        Instance = this;
        UpdatePhaseText();
        timer = GetComponent<RopeTimer>();

    }

    void Start()
    {
        //GameStart local
        if (!NetworkSessionData.IsNetworkSession)
        {
            OnGameStart();            
        }
    }

    public void OnGameStart(int? seed = null, int[] cardInHandIDs = null)
    {
        EffectProcessor.Reset();
        if (Player.Players == null || Player.Players.Length < 2)
        {
            Debug.LogError("TurnManager: need at least 2 Player instances.");
            return;
        }

        if (seed.HasValue)
        {
            for (int idx = 0; idx < Player.Players.Length; idx++)
            {
                Player p = Player.Players[idx];
                p.deck.cards.ShuffleWithSeed(seed.Value + idx);
                Debug.Log($"[DeckCheck] Player {idx} top1={p.deck.cards[0].name}, top2={p.deck.cards[1].name}");
            }
            Debug.Log($"TurnManager: Deck shuffled with seed {seed.Value}");
            
        }
        else //Shuffle Local
        {
            for (int idx = 0; idx < Player.Players.Length; idx++)
            {
                Player p = Player.Players[idx];
                p.deck.cards.Shuffle();
            }
            Debug.Log("TurnManager: Deck shuffled with random seed");
        }

        timer.StartTimer();
        CardLogic.CardsCreatedThisGame.Clear();
        CreatureLogic.CreaturesCreatedThisGame.Clear();
        BuildingLogic.BuildingsCreatedThisGame.Clear();

        foreach (Player p in Player.Players)
        {
            p.LoadCharacterInfoFromAsset();
            p.TransmitInfoAboutPlayerToVisual();
        }

        EnsurePhaseReadyMatchesPlayers();
        ResetPhaseReadyFlags();

        
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

    public void StopTheTimer()
    {
        if (timer != null)
            timer.StopTimer();
    }

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
        if (NetworkSessionData.IsNetworkSession)
        {
            Player p = (Player.Players != null && participantIndex >= 0 && participantIndex < Player.Players.Length)
                ? Player.Players[participantIndex] : null;
            bool isMyPlayer = p != null && p.MainPArea.AllowedToControlThisPlayer;

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
        if (timer != null)
            timer.StopTimer();

        bool roundEnded = currentPhase == TurnPhases.Battle;

        TurnPhases next = currentPhase switch
        {
            TurnPhases.Command => TurnPhases.Battle,
            TurnPhases.Battle  => TurnPhases.End,
            TurnPhases.End     => TurnPhases.Regroup,
            TurnPhases.Regroup => TurnPhases.Command,
            _                  => TurnPhases.Command
        };


        int newRound = roundEnded ? currentRound + 1 : currentRound;

        if (NetworkSessionData.IsNetworkSession)
        {
            // Le serveur diffuse la transition à tous les clients (y compris lui-même).
            // C'est PhaseTransitionClientRpc qui appellera EnterPhase et OnTurnEnd.
            GameNetworkManager.Instance.FlushBuffer();
            GameNetworkManager.Instance.BroadcastPhaseTransition(next, roundEnded, newRound);
        }
        else
        {
            // Mode local : comportement existant
            if (roundEnded)
            {
                foreach (Player p in Player.Players)
                    p.OnTurnEnd();
                currentRound++;
            }
            EnterPhase(next);
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
        currentPhase = phase;
        EnsurePhaseReadyMatchesPlayers();
        ResetPhaseReadyFlags();

        UpdatePhaseText();

        if (timer != null)
        {
            timer.StopTimer();
            bool timerPhase = phase == TurnPhases.Command || phase == TurnPhases.Battle;
            if (timerPhase)
                timer.StartTimer();
        }


        switch (phase)
        {
            case TurnPhases.Regroup:
                // new ShowMessageCommand("Regroup", 1.5f).AddToQueue();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnRegroupPhaseStart();
                StartCoroutine(AutoAdvanceFromRegroup());
                break;
            case TurnPhases.Command:
                // new ShowMessageCommand("Command", 1.5f).AddToQueue();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnCommandPhaseEntered();
                break;
            case TurnPhases.Battle:
                // new ShowMessageCommand("Battle", 1.5f).AddToQueue();
                if (!AnyZoneHasPossibleCombat())
                {
                    StartCoroutine(AutoAdvanceFromBattle());
                    break;
                }
                foreach (ZoneCombatResolver r in ZoneCombatResolver.AllResolvers)
                    r.OnBattlePhaseStart();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnBattlePhaseEntered();
                break;
            case TurnPhases.End:
                // new ShowMessageCommand("End", 1.5f).AddToQueue();
                foreach (ZoneCombatResolver r in ZoneCombatResolver.AllResolvers)
                    r.OnBattlePhaseEnd();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnEndPhaseEntered();
                StartCoroutine(AutoAdvanceFromEnd());
                break;
        }

        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
        RefreshAllPlayableHighlights();
    }

    void UpdatePhaseText()
    {
        if (phaseText == null)
            return;
        string label = currentPhase switch
        {
            TurnPhases.Regroup => "Regroup",
            TurnPhases.Command => "Command",
            TurnPhases.Battle => "Battle",
            TurnPhases.End => "End",
            _ => ""
        };
        phaseText.text = label;
    }

    public static void RefreshAllPlayableHighlights()
    {
        if (Command.CardDrawPending())
            return;
        if (Player.Players == null)
            return;
        if (NetworkSessionData.IsNetworkSession)
        {
            GlobalSettings.Instance.localPlayer.HighlightPlayableCards();
        }
        else
        {
            foreach (Player p in Player.Players)
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

    IEnumerator CoAdvanceRegroupWhenCommandQueueIdle()
    {
        yield return new WaitWhile(() => Command.playingQueue || Command.CardDrawPending());
        if (currentPhase != TurnPhases.Regroup)
            yield break;
        EnterPhase(TurnPhases.Command);
    }

    IEnumerator AutoAdvanceFromBattle()
    {
        yield return new WaitForSeconds(1.5f);
        if (!NetworkSessionData.IsNetworkSession || Unity.Netcode.NetworkManager.Singleton.IsServer)
            AdvancePhaseWhenAllReady();
    }

    IEnumerator AutoAdvanceFromEnd()
    {
        yield return new WaitWhile(() => Command.playingQueue);
        EnterPhase(TurnPhases.Regroup);
    }

    IEnumerator AutoAdvanceFromRegroup()
    {
        yield return new WaitWhile(() => Command.playingQueue || Command.CardDrawPending());
        yield return new WaitForSeconds(1.5f);
        EnterPhase(TurnPhases.Command);
    }
}
