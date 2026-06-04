using UnityEngine;

public abstract class AttackModifierSO : ScriptableObject
{
    public abstract void Apply(CreatureLogic attacker, CreatureLogic mainTarget);
}
