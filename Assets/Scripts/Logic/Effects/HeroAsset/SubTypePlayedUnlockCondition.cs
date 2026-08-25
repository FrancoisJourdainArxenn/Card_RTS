using UnityEngine;

public enum SubTypeCountMode
{
    Played,
    Created,
    Both
}

[CreateAssetMenu(menuName = "Heroes/Unlock Conditions/SubType Played")]
public class SubTypePlayedUnlockCondition : HeroUnlockConditionSO
{
    public SubType RequiredSubType;
    public SubTypeCountMode Mode = SubTypeCountMode.Played;
    public int Threshold;

    private int GetCount(Player owner) => Mode switch
    {
        SubTypeCountMode.Played  => owner.matchStats.GetSubTypePlayed(RequiredSubType),
        SubTypeCountMode.Created => owner.matchStats.GetSubTypeCreated(RequiredSubType),
        SubTypeCountMode.Both    => owner.matchStats.GetSubTypePlayed(RequiredSubType) + owner.matchStats.GetSubTypeCreated(RequiredSubType),
        _ => 0
    };

    public override bool IsUnlocked(Player owner) => GetCount(owner) >= Threshold;

    public override void ResetProgress(Player owner)
    {
        owner.matchStats.ResetSubTypePlayed(RequiredSubType);
        owner.matchStats.ResetSubTypeCreated(RequiredSubType);
    }

    public override string GetDescription(Player owner)
    {
        int remaining = Mathf.Max(0, Threshold - GetCount(owner));
        string verb = Mode switch
        {
            SubTypeCountMode.Played  => "Play",
            SubTypeCountMode.Created => "Create",
            SubTypeCountMode.Both    => "Play or Create",
            _ => "Get"
        };
        return $"{verb} {remaining} {RequiredSubType} to unlock me.";
    }
}
