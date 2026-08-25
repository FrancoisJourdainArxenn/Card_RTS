using UnityEngine;

[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Phase")]
public class CondPhase : ConditionSO
{
    public TurnManager.TurnPhases requiredPhase = TurnManager.TurnPhases.Battle;

    public override bool Evaluate(EffectContext context)
    {
        return TurnManager.Instance != null && TurnManager.Instance.CurrentPhase == requiredPhase;
    }
}
