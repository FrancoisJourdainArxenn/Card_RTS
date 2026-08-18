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
            MatchStatType.TokensCreated   => $"Create {remaining} Tokens to Unlock me.",
            MatchStatType.UnitsDied       => $"Wait for {remaining} Units to die to Unlock me.",
            MatchStatType.AllyUnitsDied   => $"Lose {remaining} Units to Unlock me.",
            MatchStatType.EnemyUnitsDied  => $"Kill {remaining} Enemy Units to Unlock me.",
            MatchStatType.DamageTaken          => $"Take {remaining} Damage to Unlock me.",
            MatchStatType.ShieldDamageAbsorbed => $"Absorb {remaining} Damage with Shields to Unlock me.",
            MatchStatType.DamageDealt          => $"Deal {remaining} Damage to Unlock me.",
            _ => $"{Stat} : {remaining}"
        };
    }
}
