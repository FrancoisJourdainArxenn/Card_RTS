using UnityEngine;

[CreateAssetMenu(menuName = "Effects/Conditions/Condition:SubType")]
public class CondSubType : ConditionSO
{
    public SubType RequiredSubType;

    public override bool Evaluate(EffectContext context)
    {
        if (context.EventSubjectCreature != null)
            return context.EventSubjectCreature.ca.subType == RequiredSubType;
        if (context.EventSubjectBuilding != null)
            return context.EventSubjectBuilding.ca.subType == RequiredSubType;
        return false;
    }
}
