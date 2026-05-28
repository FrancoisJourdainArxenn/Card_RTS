using UnityEngine;

public class ApplyShieldCommand : Command
{
    private readonly int targetID;
    private readonly int amount;
    private readonly EffectVisualData visualData;

    public ApplyShieldCommand(int targetID, int amount, EffectVisualData visualData)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.visualData = visualData;
    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);
        if (target != null && target.TryGetComponent(out VfxManager vfx))
            vfx.ShowShieldVfx(visualData?.vfxPrefab, amount);

        CommandExecutionComplete();
    }
}
