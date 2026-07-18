using UnityEngine;

[CreateAssetMenu(menuName = "Heroes/Unlock Conditions/Stat Threshold")]
public class StatThresholdUnlockCondition : HeroUnlockConditionSO
{
    public MatchStatType Stat;
    public int Threshold;

    public override bool IsUnlocked(Player owner) => owner.matchStats.Get(Stat) >= Threshold;

    public override void ResetProgress(Player owner) => owner.matchStats.Reset(Stat);

    public override string GetDescription(Player owner)
    {
        int remaining = Mathf.Max(0, Threshold - owner.matchStats.Get(Stat));
        return Stat switch
        {
            MatchStatType.RessourcesSpent => $"Spend {remaining} Ressources to Unlock me.",
            MatchStatType.CardsPlayed     => $"Play {remaining} Cards to Unlock me.",
            MatchStatType.UnitsSummoned   => $"Summon {remaining} Units to Unlock me.",
            _ => $"{Stat} : {remaining}"
        };
    }
}
