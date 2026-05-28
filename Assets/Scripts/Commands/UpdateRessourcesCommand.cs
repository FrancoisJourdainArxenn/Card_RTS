using UnityEngine;
using System.Collections;

public class UpdateRessourcesCommand : Command {

    private Player p;
    private int mainRessourceTotal;
    private int mainRessourceAvailable;

    public UpdateRessourcesCommand(Player p, int mainRessourceTotal, int mainRessourceAvailable)
    {
        this.p = p;
        this.mainRessourceTotal = mainRessourceTotal;
        this.mainRessourceAvailable = mainRessourceAvailable;
    }

    public override void StartCommandExecution()
    {
        
        p.mainRessourceAvailable = mainRessourceAvailable;

        if (p == GlobalSettings.Instance.localPlayer && GlobalSettings.Instance.UiPlayerVisual != null)
        {
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
        }

        if (p.baseVisual != null)
        {
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
            p.baseVisual.ApplyLookFromAsset();
        }
        CommandExecutionComplete();
    }
}
