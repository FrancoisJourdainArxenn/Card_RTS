using UnityEngine;
using System.Collections;

public class DrawACardCommand : Command {

    private Player p;
    private CardLogic cl;
    private bool fast;
    private bool fromDeck;

    public DrawACardCommand(CardLogic cl, Player p, bool fast, bool fromDeck)
    {        
        this.cl = cl;
        this.p = p;
        this.fast = fast;
        this.fromDeck = fromDeck;
    }

    public override void StartCommandExecution()
    {
        bool isLocalPlayer = GlobalSettings.Instance.localPlayer == p;
        bool isSolo = !NetworkSessionData.IsNetworkSession;

        if (isLocalPlayer || isSolo)
            p.MainPArea.handVisual.GivePlayerACard(p, cl.ca, cl.UniqueCardID, fast, fromDeck);
        else
            Command.CommandExecutionComplete();
    }
}
