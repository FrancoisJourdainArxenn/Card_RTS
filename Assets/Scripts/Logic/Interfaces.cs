using UnityEngine;
using System.Collections;

public interface ITargetableVisual
{
    void UpdateTargetableVisual(bool targetable, bool targeted = false);
    void ClearTargetableVisual();
}

public interface ILivable: IIdentifiable
{
    int Health { get; set; }
    int MaxHealth { get; set; }
    ZoneLogic Zone { get; }

    public bool IsDamaged => Health < MaxHealth;
    public bool IsShielded => false;
    public bool IsPendingDeath => false;
    // True once this entity's OnDeath has already been resolved ahead of time during battle
    // planning (see CreatureLogic.ResolvePredictedBattleDeath) — excluded from targeting just
    // like IsPendingDeath, even though the "real" death hasn't been applied yet.
    public bool OnDeathResolvedInBattle => false;
    // True while this entity is boarded aboard a Transport creature — safe from targeting/combat
    // until it disembarks. See CreatureLogic.TransportCarrierID.
    public bool IsBoarded => false;
    public bool IsMelee => false;
    public bool IsRanged => false;
    public bool IsFlying => false;
    public int Attack { get; set; }
    void Die();

    // Applies damage, respecting shield on implementors that have one.
    // Returns the actual Health after absorption. Default: no shield.
    int TakeDamage(int dmg)
    {
        Health -= dmg;
        return Health;
    }
}

public interface IIdentifiable
{
    int ID { get; }
    string DisplayName { get; }
}
