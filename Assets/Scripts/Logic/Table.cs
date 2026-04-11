using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Table : MonoBehaviour 
{
    public List<CreatureLogic> CreaturesInPlay = new List<CreatureLogic>();

    public void PlaceCreatureAt(int index, CreatureLogic creature)
    {
        CreaturesInPlay.Insert(index, creature);
    }
        
}
