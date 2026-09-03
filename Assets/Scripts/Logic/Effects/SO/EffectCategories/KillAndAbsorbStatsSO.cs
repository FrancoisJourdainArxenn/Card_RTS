using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(menuName = "Effects/KillAndAbsorbStatsSO")]
public class KillAndAbsorbStatsSO : HealthEffectSO
{
    public override EffectPriority Priority => EffectPriority.DealDamage;

    [Header("Assimilate VFX")]
    [SerializeField] private GameObject beamPrefab;

    private ILivable _source;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        Log($"[KillAndAbsorbStats] TRIGGERED — {EffectName} | source: {context.Source?.DisplayName ?? "none"}");
        _sourceID = context.Source?.ID ?? -1;
        _source = context.Source;
        base.Execute(EffectName, context, effectInfo, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        string role = !NetworkSessionData.IsNetworkSession ? "" : NetworkManager.Singleton.IsServer ? "[Server]" : "[Client]";

        if (target.Health <= 0 || _source == null)
        {
            Log($"[KillAndAbsorbStats]{role} SKIP — cible={target.DisplayName} health={target.Health} sourceNull={_source == null}");
            return;
        }
        Log($"[KillAndAbsorbStats]{role} EXECUTE — cible={target.DisplayName} health={target.Health}");

        // Beam lancé AVANT target.Health = 0 (donc avant la mise à mort et toute la cascade
        // qu'elle déclenche : triggers OnDeath alliés, animation de mort, buff visuel de la source)
        // pour être le tout premier élément visuel joué — voir Command.FlushPendingDeaths : les
        // Commands "cause" enqueuées ici jouent avant celles de la mort, mises en file plus tard
        // par l'orchestrateur (EffectRegistry.Execute).
        if (beamPrefab != null)
        {
            GameObject sourceGO = IDHolder.GetGameObjectWithID(_sourceID);
            GameObject targetGO = IDHolder.GetGameObjectWithID(target.ID);
            if (sourceGO != null && targetGO != null)
            {
                // Le beam s'ancre sur la target (voir PlayAssimilateVFXCommand) : sa visibilité
                // suit donc le fog de la zone de la target, comme les autres VFX (VfxManager, TokenGenerationSO).
                ZoneManager targetZone = targetGO.GetComponentInParent<ZoneManager>();
                bool isVisible = targetZone == null || FogOfWarManager.Instance == null
                                || !FogOfWarManager.Instance.IsZoneFogged(targetZone);
                if (isVisible)
                    new PlayAssimilateVFXCommand(beamPrefab, sourceGO.transform.position, targetGO.transform.position).AddToQueue();
            }
        }

        // Stats capturées avant la mort — vie ACTUELLE, pas la vie max.
        int stolenAttack = target.Attack;
        int stolenHealth = target.Health;

        // Mise à mort directe : ne passe pas par TakeDamage, donc aucun bouclier ne peut absorber
        // le coup et aucun DealDamageCommand (pas de VFX/nombre de dégâts) n'est mis en file.
        // Die() (ou MarkPendingDeath en combat) se déclenche normalement via le setter Health.
        target.Health = 0;

        int attackBefore = _source.Attack;
        _source.Attack += stolenAttack;
        _source.MaxHealth += stolenHealth;
        _source.Health += stolenHealth;
        int actualAttackDelta = _source.Attack - attackBefore;
        new ModifyStatsCommand(_source.ID, actualAttackDelta, _source.Attack, stolenHealth, _source.Health, EffectVisual).AddToQueue();
        Log($"[KillAndAbsorbStats] ABSORB — {_source.DisplayName} gagne +{stolenAttack} ATK / +{stolenHealth} PV de {target.DisplayName}");
    }

    protected override bool IsTargetSaturated(EffectTarget target) => true;

    public override string GetDescription() =>
        "Tue la cible et gagne son Attaque et sa Vie actuelle.";
}
