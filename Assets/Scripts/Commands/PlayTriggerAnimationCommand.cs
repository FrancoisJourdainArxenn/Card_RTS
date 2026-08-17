using UnityEngine;

public class PlayTriggerAnimationCommand : Command
{
    readonly TriggerType trigger;
    readonly EffectContext context;

    public PlayTriggerAnimationCommand(TriggerType trigger, EffectContext context)
    {
        this.trigger = trigger;
        this.context = context;
    }

    public override void StartCommandExecution()
    {
        GameObject sourceGO = context.Source != null ? IDHolder.GetGameObjectWithID(context.Source.ID) : null;
        VfxManager vfx = sourceGO != null ? sourceGO.GetComponent<VfxManager>() : null;
        GameObject prefab = TriggerAnimationLibrary.Instance != null
            ? TriggerAnimationLibrary.Instance.GetVfxPrefab(trigger)
            : null;

        if (vfx != null && prefab != null)
            vfx.PlayTriggerVfx(prefab);

        CommandExecutionComplete();
    }
}
