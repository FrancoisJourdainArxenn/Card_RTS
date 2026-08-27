using UnityEngine;

public class PlayEffectVisualCommand : Command
{
    private readonly int sourceID;
    private readonly EffectVisualData visualData;

    public PlayEffectVisualCommand(int sourceID, EffectVisualData visualData)
    {
        this.sourceID = sourceID;
        this.visualData = visualData;
    }

    public override void StartCommandExecution()
    {
        GameObject sourceGO = IDHolder.GetGameObjectWithID(sourceID);
        if (sourceGO != null && sourceGO.TryGetComponent(out VfxManager vfx))
            vfx.Play(visualData, 0);

        CommandExecutionComplete();
    }
}
