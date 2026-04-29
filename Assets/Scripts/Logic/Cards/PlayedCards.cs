using System.Collections.Generic;
using UnityEngine;

public class PlayedCards : MonoBehaviour
{
    public List<CreatureLogic> Creatures = new List<CreatureLogic>();
    public List<BuildingLogic> Buildings = new List<BuildingLogic>();

    public void PlaceCreatureAt(int index, CreatureLogic creature)
    {
        Creatures.Insert(index, creature);
    }
}
