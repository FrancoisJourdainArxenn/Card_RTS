using UnityEngine;

[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Multiple Conditions")]
public class MultipleCond : ConditionSO
{
    public enum CombineMode
    {
        All, // ET : toutes les conditions doivent être vraies
        Any, // OU : une seule condition vraie suffit
    }

    // All par défaut : les assets créés avant l'ajout de ce champ restent des ET.
    public CombineMode mode = CombineMode.All;
    public ConditionSO[] Conditions;

    public override bool Evaluate(EffectContext context)
    {
        if (mode == CombineMode.Any)
        {
            foreach (ConditionSO condition in Conditions)
            {
                if (condition != null && condition.Evaluate(context))
                    return true;
            }
            return false;
        }

        foreach (ConditionSO condition in Conditions)
        {
            if (condition != null && !condition.Evaluate(context))
                return false;
        }
        return true;
    }
}
