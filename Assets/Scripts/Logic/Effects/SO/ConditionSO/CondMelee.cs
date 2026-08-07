using UnityEngine;

[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Melee")]
public class CondMelee : ConditionSO
{
    public override bool Evaluate(EffectContext context)
    {
        if (context.EventSubjectCreature != null)
            return context.EventSubjectCreature.ca.melee;
        return false;
    }
}
