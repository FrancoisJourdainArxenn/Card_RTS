using UnityEngine;

[CreateAssetMenu(menuName = "Heroes/Unlock Conditions/SubType Played")]
public class SubTypePlayedUnlockCondition : HeroUnlockConditionSO
{
    public SubType RequiredSubType;
    public int Threshold;

    public override bool IsUnlocked(Player owner) => owner.matchStats.GetSubType(RequiredSubType) >= Threshold;

    public override void ResetProgress(Player owner) => owner.matchStats.ResetSubType(RequiredSubType);

    public override string GetDescription(Player owner)
    {
        int remaining = Mathf.Max(0, Threshold - owner.matchStats.GetSubType(RequiredSubType));
        return $"Play {remaining} {RequiredSubType} to unlock me.";
    }
}
