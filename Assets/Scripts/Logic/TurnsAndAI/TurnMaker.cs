using UnityEngine;
using System.Collections;
using Unity.Netcode;   

public abstract class TurnMaker : MonoBehaviour {

    protected Player p;

    void Awake()
    {
        p = GetComponent<Player>();
    }

    /* Not used so far */
    public virtual void OnTurnStart()
    {
        // p.OnTurnStart();
    }

    /// <summary>Round upkeep: resources and one draw for this player (both players in Regroup).</summary>
    public virtual void OnRegroupPhaseStart()
    {
        p.OnTurnStart();

        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        // Debug.Log($"[TurnMaker] OnRegroupPhaseStart — joueur: {p.name} | isLocalPlayer: {isLocalPlayer} | StartSession: {isLocalPlayer}");

        // StartSession before the draw: on the host, BroadCastDrawCard calls DrawAcardClientRpc
        // synchronously, so effects must be collected and submitted before the draw could
        // otherwise trigger RegisterEndPhase → SubmitToServer(0 effects).
        if (isLocalPlayer)
            PhaseEffectPipeline.StartSession(p, TriggerType.OnRegroup);

        if (NetworkSessionData.IsNetworkSession)
        {
            if (NetworkManager.Singleton.IsServer)
                GameNetworkManager.Instance.BroadCastDrawCard(p.playerIndex);

            if (isLocalPlayer)
                TurnManager.Instance.RegisterEndPhase(p);
        }
        else
        {
            p.DrawACard();
            TurnManager.Instance.RegisterEndPhase(p);
        }
    }

    public virtual void OnCommandPhaseEntered()
    {
        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        // Debug.Log($"[TurnMaker] OnCommandPhaseEntered — joueur: {p.name} | isLocalPlayer: {isLocalPlayer} | StartSession: {isLocalPlayer}");

        if (isLocalPlayer)
            PhaseEffectPipeline.StartSession(p, TriggerType.OnCommand);
    }

    public virtual void OnBeginCombatPhaseEntered()
    {
        // In network mode, only the local player runs the targeting session.
        // In local mode, both players run it sequentially (auto-selects for testing).
        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        if (isLocalPlayer)
            PhaseEffectPipeline.StartSession(p, TriggerType.OnBeginCombat);
    }

    public virtual void OnEndPhaseEntered()
    {
        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        if (isLocalPlayer)
            PhaseEffectPipeline.StartSession(p, TriggerType.OnBattleEnd, TriggerType.OnEndTurn);
    }

    public virtual void OnTurnEnd()
    {
        StopAllCoroutines();
        p.OnTurnEnd();
    }
}
