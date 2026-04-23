using UnityEngine;
using System.Collections;

public class BaseDieCommand : Command 
{
    private NeutralZoneController neutralBaseController;
    private int baseID;

    public BaseDieCommand(int baseID, NeutralZoneController neutralBaseController)
    {
        this.neutralBaseController = neutralBaseController;
        this.baseID = baseID;
    }

    public override void StartCommandExecution()
    {
        if (neutralBaseController != null)
            neutralBaseController.RemoveBaseWithID(baseID);
        else
            Debug.LogWarning("BaseDieCommand: neutralBaseController is null for base ID " + baseID);

        CommandExecutionComplete();
    }
}
