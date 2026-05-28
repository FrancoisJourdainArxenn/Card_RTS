using UnityEngine;

public class SpawnVFXCommand : Command
{
    private readonly GameObject prefab;
    private readonly Vector3 position;

    public SpawnVFXCommand(GameObject prefab, Vector3 position)
    {
        this.prefab = prefab;
        this.position = position;
    }

    public override void StartCommandExecution()
    {
        GameObject vfx = Object.Instantiate(prefab, position, Quaternion.identity);
        Object.Destroy(vfx, 3f);
        CommandExecutionComplete();
    }
}
