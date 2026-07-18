using UnityEngine;

[CreateAssetMenu(menuName = "Effects/Conditions/Condition:MyZone")]
public class CondMyZone : ConditionSO
{
    public override bool Evaluate(EffectContext context)
    {
        if (context.Source?.Zone == null)
            return false;

        ILivable subject = (ILivable)context.EventSubjectCreature ?? (ILivable)context.EventSubjectBuilding;
        return subject != null && subject.Zone == context.Source.Zone;
    }
}
