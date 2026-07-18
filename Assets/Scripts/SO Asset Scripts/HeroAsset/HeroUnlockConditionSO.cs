using UnityEngine;

public abstract class HeroUnlockConditionSO : ScriptableObject
{
    public abstract bool IsUnlocked(Player owner);
    public abstract string GetDescription();
}
