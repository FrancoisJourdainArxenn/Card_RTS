using UnityEngine;
using DG.Tweening;

public class RaiseEffectVisualCommand : Command
{
    readonly CardEffectData data;
    readonly EffectContext context;

    public RaiseEffectVisualCommand(CardEffectData data, EffectContext context)
    {
        this.data = data;
        this.context = context;
    }

    public override void StartCommandExecution()
    {
        TargetingVisualEvents.RaiseAutoEffectTriggered(data, context);
        DOVirtual.DelayedCall(GlobalSettings.Instance.RaiseEffectVisualDelay, CommandExecutionComplete);
    }
}
