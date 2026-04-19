using UnityEngine;
using System.Collections;

public class CreatureMoveCommand : Command
{
    private int TargetZoneID;
    private int SelectedUnitID;
    private int tablePos;

    public CreatureMoveCommand(int SelectedUnitID, int TargetZoneID, int tablePos)
    {
        this.TargetZoneID = TargetZoneID;
        this.SelectedUnitID = SelectedUnitID;
        this.tablePos = tablePos;
    }

    public override void StartCommandExecution()
    {
        GameObject movingUnit = IDHolder.GetGameObjectWithID(SelectedUnitID);
        movingUnit.GetComponent<CreatureMoveVisual>().Move(TargetZoneID, tablePos);
    }
}
