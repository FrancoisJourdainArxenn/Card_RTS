using UnityEngine;
using System.Collections;

public class BuffAttackCommand : Command {

    private int targetID;
    private int amount;
    private int attackAfter;
    private EffectVisualData visualData;


    public BuffAttackCommand( int targetID, int amount, int attackAfter, EffectVisualData visualData = null)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.attackAfter = attackAfter;
        this.visualData = visualData;

    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);
        if (target.GetComponent<OneLivableManager>() != null)
            target.GetComponent<OneLivableManager>().BuffAttack(amount, attackAfter);
        target?.GetComponent<VfxManager>()?.Play(visualData, amount);
        CommandExecutionComplete();
    }
}
