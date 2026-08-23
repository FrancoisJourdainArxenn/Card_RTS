using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class HealthEffectSO : EffectSO
{
    protected Player _caster;
    protected CardAsset _playedCard;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        _caster = context.Caster;
        _playedCard = context.PlayedCard;
        Log($"[HealthEffect] RESOLVING — {EffectName}");
        List<IIdentifiable> eligibleTargets = GetAffectedElements(context, effectInfo);
        if (eligibleTargets.Count == 0)
        {
            Log($"[HealthEffect] RESOLVED — {EffectName}: aucune cible éligible, effet annulé.");
            return;
        }

        Log($"[HealthEffect] RESOLVED — {EffectName}: {eligibleTargets.Count} cible(s) éligible(s) et affectée(s) — [{string.Join(", ", eligibleTargets.Select(t => $"{t.DisplayName}(ID:{t.ID})"))}]");
        ApplyEffect(effectInfo, eligibleTargets, visualData);
    }
}
