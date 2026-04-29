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

    public virtual void OnBattlePhaseEntered()
    {
        EffectProcessor.NotifyBattleStart(p);
    }

    public virtual void OnEndPhaseEntered()
    {
        EffectProcessor.NotifyBattleEnd(p);
        EffectProcessor.NotifyEndTurn(p);
    }

}
