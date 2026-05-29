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

        creatureToRemove.GetComponent<VfxManager>()?.PlayDeath();


        if (p.PAreas != null)
        {
            foreach (PlayerArea area in p.PAreas)
            {
                if (area == null || area.tableVisual == null)
                    continue;

                if (area.tableVisual.MeleeCreaturesOnTable.Contains(creatureToRemove) ||
                    area.tableVisual.RangedCreaturesOnTable.Contains(creatureToRemove))
                {
                    // RemoveCreatureWithID calls CommandExecutionComplete internally via its tween OnComplete.
                    area.tableVisual.RemoveCreatureWithID(DeadCreatureID);
                    return;
                }
            }
        }

        //Debug.LogWarning("CreatureDieCommand: créature " + DeadCreatureID + " introuvable sur les tables de ce joueur.");
        Object.Destroy(creatureToRemove);
        BuildSpotVisual.RefreshAll();
        Command.CommandExecutionComplete();
    }
}
