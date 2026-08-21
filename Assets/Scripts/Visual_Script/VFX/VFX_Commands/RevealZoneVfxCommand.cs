using UnityEngine;

// Zones n'ont pas d'IDHolder (voir IDHolder.GetGameObjectWithID, jamais enregistré pour ZoneManager) —
// la position se résout donc directement via ZoneManager.AllZones, comme PhaseEffectPipeline.ResolveEntityByID.
public class RevealZoneVfxCommand : Command
{
    private readonly int zoneID;
    private readonly EffectVisualData visualData;

    public RevealZoneVfxCommand(int zoneID, EffectVisualData visualData)
    {
        this.zoneID = zoneID;
        this.visualData = visualData;
    }

    public override void StartCommandExecution()
    {
        ZoneManager zone = ZoneManager.AllZones.Find(z => z.Logic.ID == zoneID);
        if (zone != null && visualData?.vfxPrefab != null)
        {
            // Caméra top-down : couché à plat comme le VFX de présence du Scan
            // (voir ScanButton.ReceiveRevealNotification, même rotation -90 sur X).
            GameObject vfx = Object.Instantiate(visualData.vfxPrefab, zone.transform.position, Quaternion.Euler(-90f, 0f, 0f));
            Object.Destroy(vfx, GetParticleLifetime(vfx));
        }

        CommandExecutionComplete();
    }

    private float GetParticleLifetime(GameObject vfx)
    {
        ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
        if (ps != null)
        {
            ParticleSystem.MainModule main = ps.main;
            return main.duration + main.startLifetime.constantMax;
        }
        return 2f;
    }
}
