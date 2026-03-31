using System.Collections;
using UnityEngine;
using TMPro;

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
        OnGameStart();
    }

    public void OnGameStart()
    {
        timer.StartTimer();
        CardLogic.CardsCreatedThisGame.Clear();
        CreatureLogic.CreaturesCreatedThisGame.Clear();

        foreach (Player p in Player.Players)
        {
            p.LoadCharacterInfoFromAsset();
            p.TransmitInfoAboutPlayerToVisual();
        }

        if (Player.Players == null || Player.Players.Length < 2)
        {
            Debug.LogError("TurnManager: need at least 2 Player instances.");
            return;
        }

        EnsurePhaseReadyMatchesPlayers();
        ResetPhaseReadyFlags();

        int i = 0;
        for (i = 0; i < initdraw; i++)
        {
            foreach (Player p in Player.Players)
                p.DrawACard(true);
        }
        if (i >= initdraw)
        {
            foreach (Player p in Player.Players)
                p.OnTurnStart();
        }
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
        EnsurePhaseReadyMatchesPlayers();
        if (phaseReady == null || participantIndex < 0 || participantIndex >= phaseReady.Length)
            return;
        if (phaseReady[participantIndex])
            return;

        phaseReady[participantIndex] = true;
        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();

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

        /* End phase — disabled for now (restore this block and Battle => End when you need End again)
        if (currentPhase == TurnPhases.End)
        {
            foreach (Player p in Player.Players)
                p.OnTurnEnd();

            currentRound++;
            EnterPhase(TurnPhases.Regroup);
            return;
        }
        */

        if (currentPhase == TurnPhases.Battle)
        {
            foreach (Player p in Player.Players)
                p.OnTurnEnd();

            currentRound++;
            EnterPhase(TurnPhases.Regroup);
            return;
        }

        TurnPhases next = currentPhase switch
        {
            TurnPhases.Regroup => TurnPhases.Command,
            TurnPhases.Command => TurnPhases.Battle,
            // TurnPhases.Battle => TurnPhases.End,
            _ => TurnPhases.Command
        };

        EnterPhase(next);
    }

    void EnterPhase(TurnPhases phase)
    {
        currentPhase = phase;
        EnsurePhaseReadyMatchesPlayers();
        ResetPhaseReadyFlags();

        UpdatePhaseText();

        if (timer != null)
        {
            timer.StopTimer();
            timer.StartTimer();
        }

        switch (phase)
        {
            case TurnPhases.Regroup:
                new ShowMessageCommand("Regroup", 1.5f).AddToQueue();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnRegroupPhaseStart();
                break;
            case TurnPhases.Command:
                new ShowMessageCommand("Command", 1.5f).AddToQueue();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnCommandPhaseEntered();
                break;
            case TurnPhases.Battle:
                new ShowMessageCommand("Battle", 1.5f).AddToQueue();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnBattlePhaseEntered();
                break;
            case TurnPhases.End:
                new ShowMessageCommand("End", 1.5f).AddToQueue();
                foreach (Player p in Player.Players)
                    p.GetComponent<TurnMaker>().OnEndPhaseEntered();
                break;

        }

        RefreshAllPlayableHighlights();
        if (GlobalSettings.Instance != null)
            GlobalSettings.Instance.RefreshEndPhaseButtons();
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
            // TurnPhases.End => "End",
            _ => ""
        };
        phaseText.text = label;
    }

    public static void RefreshAllPlayableHighlights()
    {
        if (Player.Players == null)
            return;
        foreach (Player p in Player.Players)
        {
            if (p != null)
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
}
