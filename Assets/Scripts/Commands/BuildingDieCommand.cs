using UnityEngine;
using System.Collections;

public class BuildingDieCommand : Command 
{
    private Player p;
    private OneBuildingManager buildingManager;
    private int buildingID;

    public BuildingDieCommand(int buildingID, Player p, OneBuildingManager buildingManager)
    {
        this.p = p;
        this.buildingID = buildingID;
        this.buildingManager = buildingManager;
    }

    public override void StartCommandExecution()
    {
        buildingManager.RemoveBuildingWithID(buildingID);
    }
}
