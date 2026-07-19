using UnityEngine;

[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Multiple Conditions")]
public class MultipleCond : ConditionSO
{
    public ConditionSO[] Conditions;

    public override bool Evaluate(EffectContext context)
    {
        foreach (ConditionSO condition in Conditions)
        {
            if (condition != null && !condition.Evaluate(context))
                return false;
        }
        return true;
    }
}
