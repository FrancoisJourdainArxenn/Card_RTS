using UnityEngine;
using System.Collections.Generic;

public enum MatchStatType
{
    RessourcesSpent,
    CardsPlayed,
    UnitsSummoned,
    TokensCreated,
    UnitsDied,       // All : toute unité qui meurt, peu importe le camp
    AllyUnitsDied,   // Ally : mes unités à moi qui meurent
    EnemyUnitsDied,  // Enemy : les unités adverses qui meurent
    DamageTaken,          // dégâts effectifs subis (après bouclier) par mes unités
    ShieldDamageAbsorbed, // dégâts absorbés par mes boucliers
    DamageDealt,          // dégâts effectifs infligés (combat + effets) par mes unités/sorts
    // ajoutez ici les compteurs dont vous aurez besoin pour vos futures conditions de héros
}

public class HeroCountUnlock
{
    public string OwnerLabel = "Unknown";
    private readonly Dictionary<MatchStatType, int> _values = new();

    public int Get(MatchStatType stat) => _values.GetValueOrDefault(stat, 0);

    public void Add(MatchStatType stat, int amount = 1)
    {
        if (amount == 0) return;
        _values[stat] = Get(stat) + amount;
        // Debug.Log($"[HeroCountUnlock:{OwnerLabel}] {stat} +{amount} => {_values[stat]}");
    }

    public void Reset()
    {
        _values.Clear();
        // Debug.Log("[HeroCountUnlock] Reset");
    }

    public void Reset(MatchStatType stat)
    {
        _values[stat] = 0;
        // Debug.Log($"[HeroCountUnlock:{OwnerLabel}] {stat} reset to 0");
    }

    private readonly Dictionary<SubType, int> _subTypeValues = new();

    public int GetSubType(SubType subType) => _subTypeValues.GetValueOrDefault(subType, 0);

    public void AddSubType(SubType subType, int amount = 1)
    {
        if (amount == 0) return;
        _subTypeValues[subType] = GetSubType(subType) + amount;
        // Debug.Log($"[HeroCountUnlock:{OwnerLabel}] SubType {subType} +{amount} => {_subTypeValues[subType]}");
    }

    public void ResetSubType(SubType subType)
    {
        _subTypeValues[subType] = 0;
        // Debug.Log($"[HeroCountUnlock:{OwnerLabel}] SubType {subType} reset to 0");
    }
}
