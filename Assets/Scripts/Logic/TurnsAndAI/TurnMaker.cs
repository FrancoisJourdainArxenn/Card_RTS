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
        EffectProcessor.NotifyRegroup(p);
        if(NetworkSessionData.IsNetworkSession)
        {
            if(NetworkManager.Singleton.IsServer)
                GameNetworkManager.Instance.BroadCastDrawCard(p.playerIndex);
        }
        else
        {
            p.DrawACard();
        }
    }

    public virtual void OnCommandPhaseEntered()
    {
        EffectProcessor.NotifyCommand(p);
    }

    public virtual void OnBeginCombatPhaseEntered()
    {
        // In network mode, only the local player runs the targeting session.
        // In local mode, both players run it sequentially (auto-selects for testing).
        bool isLocalPlayer = !NetworkSessionData.IsNetworkSession
            || p.MainPArea.AllowedToControlThisPlayer;

        if (isLocalPlayer)
            BeginCombatEffectManager.StartSession(p);
    }

    public virtual void OnEndPhaseEntered()
    {
        EffectProcessor.NotifyBattleEnd(p);
        EffectProcessor.NotifyEndTurn(p);
    }

}
