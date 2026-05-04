using UnityEngine;
using System.Collections;

public class DealDamageCommand : Command {

    private int targetID;
    private int amount;
    private int healthAfter;

    public DealDamageCommand( int targetID, int amount, int healthAfter)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.healthAfter = healthAfter;
    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);
        if (targetID == GlobalSettings.Instance.LowPlayer.PlayerID || targetID == GlobalSettings.Instance.TopPlayer.PlayerID)
        {
            // target is a hero
            target.GetComponent<MainBaseVisual>().TakeDamage(amount,healthAfter);
        }
        else if (target != null && target.GetComponent<OneBaseManager>() != null)
        {
            target.GetComponent<OneBaseManager>().TakeDamage(amount, healthAfter);
        }
        else if (target != null && target.GetComponent<OneBuildingManager>() != null)
        {
            target.GetComponent<OneBuildingManager>().TakeDamage(amount, healthAfter);
        }
        else
        {
            target?.GetComponent<OneCreatureManager>()?.TakeDamage(amount, healthAfter);
        }
        CommandExecutionComplete();
    }
}
