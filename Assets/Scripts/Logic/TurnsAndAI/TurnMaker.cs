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
        if (NetworkSessionData.IsNetworkSession)
        {
            if (NetworkManager.Singleton.IsServer)
                GameNetworkManager.Instance.BroadCastDrawCard(p.playerIndex);
        }
        else
        {
            p.DrawACard();
        }
        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        if (isLocalPlayer)
            EffectTargetingManager.StartSession(p, TriggerType.OnRegroup);
    }

    public virtual void OnCommandPhaseEntered()
    {
        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        if (isLocalPlayer)
            EffectTargetingManager.StartSession(p, TriggerType.OnCommand);
    }

    public virtual void OnBeginCombatPhaseEntered()
    {
        // In network mode, only the local player runs the targeting session.
        // In local mode, both players run it sequentially (auto-selects for testing).
        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        if (isLocalPlayer)
            EffectTargetingManager.StartSession(p, TriggerType.OnBeginCombat);
    }

    public virtual void OnEndPhaseEntered()
    {
        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        if (isLocalPlayer)
            EffectTargetingManager.StartSession(p, TriggerType.OnBattleEnd, TriggerType.OnEndTurn);
    }

}
