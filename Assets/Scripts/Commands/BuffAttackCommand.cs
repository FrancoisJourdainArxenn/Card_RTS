using UnityEngine;
using System.Collections;

public class BuffAttackCommand : Command {

    private int targetID;
    private int amount;
    private int attackAfter;

    public BuffAttackCommand( int targetID, int amount, int attackAfter)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.attackAfter = attackAfter;
    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);
        if (target.GetComponent<OneLivableManager>() != null)
            target.GetComponent<OneLivableManager>().BuffAttack(amount, attackAfter);
        
        CommandExecutionComplete();
    }
}
