using UnityEngine;
using System.Collections;
using DG.Tweening;

public class DrawACardCommand : Command {

    private Player p;
    private CardLogic cl;
    private bool fast;
    private bool fromDeck;
    private EffectVisualData visualData;

    public DrawACardCommand(CardLogic cl, Player p, bool fast, bool fromDeck, EffectVisualData visualData = null)
    {
        this.cl = cl;
        this.p = p;
        this.fast = fast;
        this.fromDeck = fromDeck;
        this.visualData = visualData;
    }

    public override void StartCommandExecution()
    {
        bool isLocalPlayer = GlobalSettings.Instance.localPlayer == p;
        bool isSolo = !NetworkSessionData.IsNetworkSession;

        if (isLocalPlayer || isSolo)
        {
            if (visualData?.vfxPrefab != null)
            {
                Vector3 spawnPos = p.MainPArea.handVisual.OtherCardDrawSourceTransform.position;
                GameObject vfx = Object.Instantiate(visualData.vfxPrefab, spawnPos, Quaternion.identity);
                Object.Destroy(vfx, 3f);
                DOVirtual.DelayedCall(0.9f, () => p.MainPArea.handVisual.GivePlayerACard(p, cl.ca, cl.UniqueCardID, false, fromDeck));
            }
            else
            {
                p.MainPArea.handVisual.GivePlayerACard(p, cl.ca, cl.UniqueCardID, fast, fromDeck);
            }
        }
        else
            Command.CommandExecutionComplete();
    }
}
