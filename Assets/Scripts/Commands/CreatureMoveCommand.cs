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
        // movingUnit.GetComponent<CreatureMoveVisual>().Move(TargetZoneID, tablePos);
        if (movingUnit == null)
        {
            Debug.LogError($"[CreatureMoveCommand] Visuel introuvable pour créature id={SelectedUnitID}. La logique a déjà été mise à jour (BaseID/fog), seul le visuel manque.");
            Command.CommandExecutionComplete();
            return;
        }
        CreatureMoveVisual moveVisual = movingUnit.GetComponent<CreatureMoveVisual>();
        if (moveVisual == null)
        {
            Debug.LogError($"[CreatureMoveCommand] Composant CreatureMoveVisual manquant sur créature id={SelectedUnitID}.");
            Command.CommandExecutionComplete();
            return;
        }
        moveVisual.Move(TargetZoneID, tablePos);
    }
}
