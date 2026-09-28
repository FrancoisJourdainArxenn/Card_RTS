using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Accorde une liste d'effets à des créatures, comme s'ils étaient écrits sur leur carte (voir
// EffectRegistry.ApplyGrantedEffects) : aux cibles actuelles (ciblage normal de l'effet) et, comme
// ModifyStatsSO, à toute créature du Caster qui entrera en jeu plus tard et correspondra à
// PersistentFilter / MatchSameNameAsTarget. Ex: "Vos home bases gagnent Scout pour le reste de la
// partie" = ciblage Creature / Friendly / HomeBase + PersistentFilter HomeBase.asset.
[CreateAssetMenu(menuName = "Effects/GrantEffectsSO")]
public class GrantEffectsSO : EffectSO
{
    [Header("Granted Effects")]
    [Tooltip("OnPlay = appliqué une fois à l'unité qui reçoit (\"gagne X\"), avec elle comme source. Passive = aura portée par l'unité (\"confère X\"), retirée à sa mort. Tout autre trigger est ignoré.")]
    public List<CardEffectData> GrantedEffects = new List<CardEffectData>();

    [Header("Permanent Type/Name Grant (rest of the game)")]
    [Tooltip("Ex: HomeBase.asset — toute unité créée plus tard qui correspond recevra aussi ces effets.")]
    public CardFilterSO PersistentFilter;
    [Tooltip("La cible touchée maintenant + toute unité créée plus tard du même nom recevront aussi ces effets.")]
    public bool MatchSameNameAsTarget;

    public override EffectPriority Priority => EffectPriority.ModifyStats;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        Log($"{EffectName}: Execution");
        _sourceID = context.Source?.ID ?? -1;

        List<IIdentifiable> affectedElements = GetAffectedElements(context, effectInfo);
        if (affectedElements.Count == 0)
        {
            Log($"{EffectName}: no eligible targets found right now.");
        }
        else
        {
            Log($"{EffectName}: granting {GrantedEffects.Count} effect(s) to {affectedElements.Count} target(s) — {string.Join(", ", affectedElements.Select(t => t.DisplayName))}");
            ApplyEffect(effectInfo, affectedElements, visualData);
        }

        // Enregistré même si personne n'est éligible maintenant — même raison que ModifyStatsSO.Execute.
        RegisterPersistentGrantIfNeeded(context, affectedElements);
    }

    // Stocké côté Player (pas sur un asset, partagé entre joueurs), comme les buffs permanents de
    // ModifyStatsSO : seules les futures créatures du Caster en profitent.
    private void RegisterPersistentGrantIfNeeded(EffectContext context, List<IIdentifiable> affectedElements)
    {
        if (context.Caster == null) return;
        if (PersistentFilter == null && !MatchSameNameAsTarget) return;

        CardFilterSO filter = PersistentFilter;
        if (MatchSameNameAsTarget)
        {
            CreatureLogic firstCreature = affectedElements.OfType<CreatureLogic>().FirstOrDefault();
            if (firstCreature == null) return;

            filter = ScriptableObject.CreateInstance<CardFilterSO>();
            filter.filterByName = true;
            filter.requiredName = CardFilterSO.EffectiveName(firstCreature.ca);
        }

        context.Caster.permanentCreatureGrants.Add(new PermanentCreatureGrant
        {
            filter = filter,
            effects = GrantedEffects,
        });
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? _ = null)
    {
        if (target is CreatureLogic creature)
            EffectRegistry.ApplyGrantedEffects(creature, GrantedEffects);
    }

    protected override bool IsTargetSaturated(EffectTarget target) => false;
}
