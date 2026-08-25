using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/RevealZoneSO")]
public class RevealZoneSO : EffectSO
{
    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        // Execute() runs deterministically on every client (see PhaseEffectPipeline.ApplyCanonicalResolution),
        // but ForceRevealedZones is a per-client fog override with no notion of "owner" — without this
        // guard, the opponent's own client would also unfog the zone for itself. Only the caster's own
        // client should apply the reveal locally (same reasoning as UiPlayerVisual — see memory
        // project_multiplayer_ui.md).
        if (context.Caster != GlobalSettings.Instance.localPlayer)
            return;

        foreach (ZoneLogic zone in GetAffectedElements(context, effectInfo).OfType<ZoneLogic>())
        {
            Log($"{EffectName}: reveal zone {zone.DisplayName}");
            int zoneID = zone.ID;
            FogOfWarManager.ForceRevealedZones.Add(zoneID);
            TempEffectTracker.Register(zoneID, () => FogOfWarManager.ForceRevealedZones.Remove(zoneID));
            new RevealZoneVfxCommand(zoneID, visualData).AddToQueue();
        }
        FogOfWarManager.Refresh();
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() => "Révèle une zone jusqu'au prochain round";
}
