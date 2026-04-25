using UnityEngine;
using System.Collections;

public class BuildingDieCommand : Command
{
    private int deadBuildingID;

    public BuildingDieCommand(int buildingID)
    {
        this.deadBuildingID = buildingID;
    }

    public override void StartCommandExecution()
    {
        GameObject buildingGO = IDHolder.GetGameObjectWithID(deadBuildingID);
        if (buildingGO == null)
        {
            CommandExecutionComplete();
            return;
        }

        OneBuildingManager manager = buildingGO.GetComponent<OneBuildingManager>();
        manager?.OriginSpot?.OnBuildingDestroyed();
        Object.Destroy(buildingGO);
        CommandExecutionComplete();
    }
}

