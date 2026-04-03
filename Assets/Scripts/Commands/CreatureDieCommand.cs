using UnityEngine;
using System.Collections;

public class CreatureDieCommand : Command 
{
    private Player p;
    private int DeadCreatureID;

    public CreatureDieCommand(int CreatureID, Player p)
    {
        this.p = p;
        this.DeadCreatureID = CreatureID;
    }

    public override void StartCommandExecution()
    {
        GameObject creatureToRemove = IDHolder.GetGameObjectWithID(DeadCreatureID);
        if (creatureToRemove == null)
        {
            Command.CommandExecutionComplete();
            return;
        }

        if (p.PAreas != null)
        {
            foreach (PlayerArea area in p.PAreas)
            {
                if (area == null || area.tableVisual == null)
                    continue;

                if (area.tableVisual.CreaturesOnTable.Contains(creatureToRemove))
                {
                    area.tableVisual.RemoveCreatureWithID(DeadCreatureID);
                    return;
                }
            }
        }

        //Debug.LogWarning("CreatureDieCommand: créature " + DeadCreatureID + " introuvable sur les tables de ce joueur.");
        Object.Destroy(creatureToRemove);
        Command.CommandExecutionComplete();
    }
}
