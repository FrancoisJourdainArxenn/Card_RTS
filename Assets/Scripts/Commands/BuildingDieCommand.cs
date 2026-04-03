using UnityEngine;
using System.Collections;

public class BuildingDieCommand : Command 
{
    private NeutralBaseController neutralBaseController;
    private int buildingID;

    public BuildingDieCommand(int buildingID, NeutralBaseController neutralBaseController)
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
