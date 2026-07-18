using UnityEngine;
using System.Collections.Generic;

public enum MatchStatType
{
    DamageDealt,
    // CreaturesKilled, //to do later if needed
    RessourcesSpent,
    CardsPlayed,
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
        Debug.Log($"[HeroCountUnlock:{OwnerLabel}] {stat} +{amount} => {_values[stat]}");
    }

    public void Reset()
    {
        _values.Clear();
        Debug.Log("[HeroCountUnlock] Reset");
    }
}
