using UnityEngine;
using System.Collections;

public interface ILivable: IIdentifiable
{
    int Health { get; set; }
    ZoneLogic Zone { get; }

    void Die();
}

public interface IIdentifiable
{
    int ID { get; }
}
