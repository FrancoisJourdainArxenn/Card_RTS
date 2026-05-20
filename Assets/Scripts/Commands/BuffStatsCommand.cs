using UnityEngine;
using System.Collections;

public class BuffStatsCommand : Command {

    private int targetID;
    private int amount;
    private int attackAfter;
    private EffectVisualData visualData;


    public BuffStatsCommand( int targetID, int amount, int attackAfter, EffectVisualData visualData = null)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.attackAfter = attackAfter;
        this.visualData = visualData;

    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);
        if (target == null)
        {
            CommandExecutionComplete();
            return;
        }
        if (target.TryGetComponent(out OneLivableManager livable))
            livable.BuffAttack(amount, attackAfter);
        if (target.TryGetComponent(out VfxManager vfx))
            vfx.Play(visualData, amount);
        CommandExecutionComplete();
    }
}
