using UnityEngine;
using System.Collections;

public class ModifyStatsCommand : Command {

    private readonly int targetID;
    private readonly int attackAmount;
    private readonly int attackAfter;
    private readonly int secondAmount;
    private readonly int healthAfter;
    private readonly EffectVisualData visualData;

    public ModifyStatsCommand(int targetID, int attackAmount, int attackAfter, int secondAmount, int healthAfter, EffectVisualData visualData = null)
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
        {
            if (attackAmount != 0) vfx.Play(visualData, attackAmount);
            if (secondAmount != 0) vfx.PlaySecond(visualData, secondAmount);
        }
        CommandExecutionComplete();
    }
}
