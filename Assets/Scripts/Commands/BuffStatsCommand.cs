using UnityEngine;
using System.Collections;

public class BuffStatsCommand : Command {

    private readonly int targetID;
    private readonly int attackAmount;
    private readonly int attackAfter;
    private readonly int secondAmount;
    private readonly int healthAfter;
    private readonly EffectVisualData visualData;

    public BuffStatsCommand(int targetID, int attackAmount, int attackAfter, int secondAmount, int healthAfter, EffectVisualData visualData = null)
    {
        this.targetID = targetID;
        this.attackAmount = attackAmount;
        this.attackAfter = attackAfter;
        this.secondAmount = secondAmount;
        this.healthAfter = healthAfter;
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
            livable.BuffStats(attackAmount, secondAmount, attackAfter, healthAfter);
        if (target.TryGetComponent(out VfxManager vfx))
            vfx.Play(visualData, attackAmount + secondAmount);
        CommandExecutionComplete();
    }
}
