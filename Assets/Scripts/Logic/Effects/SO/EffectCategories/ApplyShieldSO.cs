// using UnityEngine;

// [CreateAssetMenu(menuName = "Effects/ApplyShieldSO")]
// public class ApplyShieldSO : EffectSO
// {
//     public override void Execute(
//         string EffectName,
//         EffectContext context,
//         EffectInfo effectInfo,
//         EffectParameters parameters,
//         EffectVisualData visualData
//     )
//     {
//         Log($"{EffectName}: Execution");
//         var targets = GetAffectedElements(context, effectInfo);
//         if (targets.Count == 0)
//         {
//             Log($"{EffectName}: no eligible targets found, effect cancelled.");
//             return;
//         }
//         ApplyEffect(effectInfo, targets, parameters, visualData);
//     }

//     protected override void ApplyToTarget(ILivable target, int amount, EffectVisualData visualData)
//     {
//         new ApplyShieldCommand(target.ID, amount, visualData).AddToQueue();
//         if (target is CreatureLogic creature)
//             creature.ApplyShield(amount);
//     }

//     protected override bool IsTargetSaturated(EffectTarget target) => false;

//     public override string GetDescription(EffectParameters parameters) =>
//         $"Donne Bouclier {parameters.Amount} à la cible";
// }
