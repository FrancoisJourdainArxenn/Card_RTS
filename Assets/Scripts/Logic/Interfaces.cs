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
    public bool IsMelee => false;
    public bool IsRanged => false;
    public int Attack { get; set; }
    void Die();
}

public interface IIdentifiable
{
    int ID { get; }
    string DisplayName { get; }
}
