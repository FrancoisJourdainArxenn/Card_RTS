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
    UnitsUpgraded,        // somme des +ATK/+Vie conférés à mes unités via ModifyStats (upgrades uniquement, pas les debuffs)
    RangedUnitsSummoned,  // unités Ranged créées (jouées ou tokens), toutes sources confondues
    MeleeUnitsSummoned,   // unités Melee créées (jouées ou tokens), toutes sources confondues
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

    private readonly Dictionary<SubType, int> _subTypePlayedValues = new();

    public int GetSubTypePlayed(SubType subType) => _subTypePlayedValues.GetValueOrDefault(subType, 0);

    public void AddSubTypePlayed(SubType subType, int amount = 1)
    {
        if (amount == 0) return;
        _subTypePlayedValues[subType] = GetSubTypePlayed(subType) + amount;
        // Debug.Log($"[HeroCountUnlock:{OwnerLabel}] SubType Played {subType} +{amount} => {_subTypePlayedValues[subType]}");
    }

    public void ResetSubTypePlayed(SubType subType)
    {
        _subTypePlayedValues[subType] = 0;
        // Debug.Log($"[HeroCountUnlock:{OwnerLabel}] SubType Played {subType} reset to 0");
    }

    private readonly Dictionary<SubType, int> _subTypeCreatedValues = new();

    public int GetSubTypeCreated(SubType subType) => _subTypeCreatedValues.GetValueOrDefault(subType, 0);

    public void AddSubTypeCreated(SubType subType, int amount = 1)
    {
        if (amount == 0) return;
        _subTypeCreatedValues[subType] = GetSubTypeCreated(subType) + amount;
        // Debug.Log($"[HeroCountUnlock:{OwnerLabel}] SubType Created {subType} +{amount} => {_subTypeCreatedValues[subType]}");
    }

    public void ResetSubTypeCreated(SubType subType)
    {
        _subTypeCreatedValues[subType] = 0;
        // Debug.Log($"[HeroCountUnlock:{OwnerLabel}] SubType Created {subType} reset to 0");
    }
}
