using UnityEngine;

public class CreatureDisembarkCommand : Command
{
    private int passengerUniqueID;
    private int targetBaseID;
    private int tablePos;

    public CreatureDisembarkCommand(int passengerUniqueID, int targetBaseID, int tablePos)
    {
        this.passengerUniqueID = passengerUniqueID;
        this.targetBaseID = targetBaseID;
        this.tablePos = tablePos;
    }

    public override void StartCommandExecution()
    {
        GameObject passenger = IDHolder.GetGameObjectWithID(passengerUniqueID);
        if (passenger == null)
        {
            Command.CommandExecutionComplete();
            return;
        }

        CreatureMoveVisual moveVisual = passenger.GetComponent<CreatureMoveVisual>();
        if (moveVisual == null)
        {
            Command.CommandExecutionComplete();
            return;
        }
        moveVisual.Disembark(targetBaseID, tablePos);
        Command.CommandExecutionComplete();
    }
}
