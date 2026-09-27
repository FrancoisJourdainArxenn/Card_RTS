using UnityEngine;

// Fait avancer de N tours les effets périodiques d'une cible (créature) — utile pour les unités à
// production périodique pilotées par un CondCounter (WrappedCondition, resetCount) sur un effet
// (ex: Research Lab / Every3Turns, un Unit avec IsStructureUnit=true). Rejoue EffectRegistry.Execute()
// sur cet effet plutôt que de toucher directement le compteur interne de CondCounter
// (CreatureLogic.IncrementConditionCounter) : ce
// compteur n'est vérifié par rapport au seuil qu'à l'intérieur de ConditionSO.Evaluate(), donc
// l'incrémenter à la main sans repasser par Evaluate() désynchroniserait sa parité et retarderait
// la prochaine production au lieu de l'avancer. targetTrigger doit matcher le Trigger réellement
// utilisé par la production périodique de la cible (Research Lab est passé de OnRegroup à OnCommand).
//
// La sélection effective (une cible choisie par le joueur, toutes les cibles éligibles, une seule
// tirée au hasard...) passe par ApplyEffect/effectInfo.repartition comme les autres EffectSO — cet
// effet ne fait qu'appliquer l'avancement de tour à CHAQUE cible qu'ApplyToTarget reçoit.
[CreateAssetMenu(menuName = "Effects/AdvanceProductionSO")]
public class AdvanceProductionSO : EffectSO
{
    [Header("Parameters")]
    [Tooltip("Nombre de tours à faire avancer (1 = 'réduit le temps de production de 1 tour').")]
    public int turnsToSkip = 1;
    [Tooltip("Trigger de l'effet périodique à avancer sur la cible (doit matcher son CardEffectData.Trigger, ex: OnCommand pour Research Lab).")]
    public TriggerType targetTrigger = TriggerType.OnRegroup;

    public override EffectPriority Priority => EffectPriority.ModifyStats;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        System.Collections.Generic.List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }

        Log($"{EffectName}: advancing {turnsToSkip} turn(s), repartition={effectInfo.repartition}, {affectedElements.Count} eligible target(s).");
        ApplyEffect(effectInfo, affectedElements, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        CardAsset ca;
        Player owner;
        ILivable source;
        switch (target)
        {
            case CreatureLogic c: ca = c.ca; owner = c.owner; source = c; break;
            default: return;
        }
        if (ca.Effects == null) return;

        Log($"AdvanceProductionSO: advancing {target.DisplayName} by {turnsToSkip} turn(s).");

        for (int tick = 0; tick < turnsToSkip; tick++)
            foreach (CardEffectData data in ca.Effects)
            {
                if (data.Trigger != targetTrigger) continue;
                EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = source });
            }
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        turnsToSkip == 1 ? "Réduit de 1 tour le temps de production" : $"Réduit de {turnsToSkip} tours le temps de production";
}
