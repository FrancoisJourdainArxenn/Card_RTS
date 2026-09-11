using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Fait avancer de N tours les effets OnRegroup d'une cible (créature ou bâtiment) — utile pour les
// unités/bâtiments à production périodique pilotés par un CondCounter (WrappedCondition, resetCount)
// sur leur effet OnRegroup (ex: Research Lab / Every3Turns, un Unit avec IsStructureUnit=true, pas un
// CardType.Building). Rejoue EffectRegistry.Execute() sur cet effet plutôt que de toucher directement
// le compteur interne de CondCounter (CreatureLogic/BuildingLogic.IncrementConditionCounter) : ce
// compteur n'est vérifié par rapport au seuil qu'à l'intérieur de ConditionSO.Evaluate(), donc
// l'incrémenter à la main sans repasser par Evaluate() désynchroniserait sa parité et retarderait
// la prochaine production au lieu de l'avancer.
[CreateAssetMenu(menuName = "Effects/AdvanceProductionSO")]
public class AdvanceProductionSO : EffectSO
{
    [Header("Parameters")]
    [Tooltip("Nombre de tours à faire avancer (1 = 'réduit le temps de production de 1 tour').")]
    public int turnsToSkip = 1;

    public override EffectPriority Priority => EffectPriority.ModifyStats;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found, effect cancelled.");
            return;
        }

        Log($"{EffectName}: advancing {turnsToSkip} turn(s) on {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");

        foreach (IIdentifiable target in affectedElements)
        {
            CardAsset ca;
            Player owner;
            ILivable source;
            switch (target)
            {
                case CreatureLogic c: ca = c.ca; owner = c.owner; source = c; break;
                case BuildingLogic b: ca = b.ca; owner = b.owner; source = b; break;
                default: continue;
            }
            if (ca.Effects == null) continue;

            for (int tick = 0; tick < turnsToSkip; tick++)
                foreach (CardEffectData data in ca.Effects)
                {
                    if (data.Trigger != TriggerType.OnRegroup) continue;
                    EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = source });
                }
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        turnsToSkip == 1 ? "Réduit de 1 tour le temps de production" : $"Réduit de {turnsToSkip} tours le temps de production";
}
