using UnityEngine;
using System.Collections;

public class BaseDieCommand : Command 
{
    private NeutralZoneController neutralBaseController;
    private int buildingID;

    public BaseDieCommand(int buildingID, NeutralZoneController neutralBaseController)
    {
        this.neutralBaseController = neutralBaseController;
        this.buildingID = buildingID;
    }

    public override void StartCommandExecution()
    {
        if (neutralBaseController != null)
            neutralBaseController.RemoveBuildingWithID(buildingID);
        else
            Debug.LogWarning("BuildingDieCommand: neutralBaseController is null for building ID " + buildingID);

        CommandExecutionComplete();
    }
}
