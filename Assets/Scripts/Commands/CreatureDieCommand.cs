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

        VfxManager vfx = creatureToRemove.GetComponent<VfxManager>();
        float deathVfxDuration = vfx != null ? vfx.PlayDeath() : 0f;

        if (p.PAreas != null)
        {
            foreach (PlayerArea area in p.PAreas)
            {
                if (area == null || area.tableVisual == null)
                    continue;

                if (area.tableVisual.MeleeCreaturesOnTable.Contains(creatureToRemove) ||
                    area.tableVisual.RangedCreaturesOnTable.Contains(creatureToRemove))
                {
                    // La créature disparaît immédiatement ; seul le re-order de la rangée attend la
                    // durée du VFX de mort (voir TableVisual.RemoveCreatureWithID), qui appelle
                    // CommandExecutionComplete lui-même une fois le repositionnement terminé.
                    area.tableVisual.RemoveCreatureWithID(DeadCreatureID, deathVfxDuration);
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
