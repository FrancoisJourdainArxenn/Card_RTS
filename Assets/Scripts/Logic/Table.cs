using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Table : MonoBehaviour 
{
    public List<CreatureLogic> CreaturesInPlay = new List<CreatureLogic>();
    public List<BuildingLogic> BuildingsInPlay = new List<BuildingLogic>();

    public void PlaceCreatureAt(int index, CreatureLogic creature)
    {
        CreaturesInPlay.Insert(index, creature);
    }
        
}
