using UnityEngine;

// Embuscade — règles et calcul dans AmbushTracker. Utilisable avec n'importe quel trigger : ne lit que le
// statut de la Source (l'unité qui porte l'effet), valable de l'entrée en phase Battle jusqu'au Regroup
// suivant, et seulement tant qu'elle est encore dans la zone où elle est entrée (plus après avoir été
// renvoyée vers sa destination à l'issue d'un croisement).
[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Ambush")]
public class CondAmbush : ConditionSO
{
    public override bool Evaluate(EffectContext context)
    {
        CreatureLogic creature = context.Source as CreatureLogic;
        return creature != null && creature.IsAmbushing && creature.Zone == creature.ZoneEnteredByMove;
    }
}
