using UnityEngine;

public class CreatureBoardCommand : Command
{
    private int passengerUniqueID;
    private int transportUniqueID;

    public CreatureBoardCommand(int passengerUniqueID, int transportUniqueID)
    {
        this.passengerUniqueID = passengerUniqueID;
        this.transportUniqueID = transportUniqueID;
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
        moveVisual.Board(transportUniqueID);
        Command.CommandExecutionComplete();
    }
}
