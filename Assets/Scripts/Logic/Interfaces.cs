using UnityEngine;
using System.Collections;

public interface ITargetable
{
    void UpdateTargetableVisual(bool targetable, bool targeted = false);
    void ClearTargetableVisual();
}

public interface ILivable: IIdentifiable
{
    int Health { get; set; }
    ZoneLogic Zone { get; }

    void Die();
}

public interface IIdentifiable
{
    int ID { get; }
    string DisplayName { get; }
}
