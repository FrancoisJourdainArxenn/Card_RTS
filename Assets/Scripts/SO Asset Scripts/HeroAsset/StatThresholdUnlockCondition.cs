using UnityEngine;

[CreateAssetMenu(menuName = "Heroes/Unlock Conditions/Stat Threshold")]
public class StatThresholdUnlockCondition : HeroUnlockConditionSO
{
    public MatchStatType Stat;
    public int Threshold;

    public override bool IsUnlocked(Player owner) => owner.matchStats.Get(Stat) >= Threshold;

    public override string GetDescription() => $"{Stat} : {Threshold}";
}
