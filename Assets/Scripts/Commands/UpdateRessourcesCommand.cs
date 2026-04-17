using UnityEngine;
using System.Collections;

public class UpdateRessourcesCommand : Command {

    private Player p;
    private int mainRessourceTotal;
    private int mainRessourceAvailable;
    private int secondRessourceTotal;
    private int secondRessourceAvailable;

    public UpdateRessourcesCommand(Player p, int mainRessourceTotal, int mainRessourceAvailable, int secondRessourceTotal, int secondRessourceAvailable)
    {
        this.p = p;
        this.mainRessourceTotal = mainRessourceTotal;
        this.mainRessourceAvailable = mainRessourceAvailable;
        this.secondRessourceTotal = secondRessourceTotal;
        this.secondRessourceAvailable = secondRessourceAvailable;
    }

    public override void StartCommandExecution()
    {
        
        p.mainRessourceAvailable = mainRessourceAvailable;
        p.secondRessourceAvailable = secondRessourceAvailable;

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
