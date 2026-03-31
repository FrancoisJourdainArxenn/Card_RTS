using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class IDHolder : MonoBehaviour {

    public int UniqueID;
    private static List<IDHolder> allIDHolders = new List<IDHolder>();

    void Awake()
    {
        allIDHolders.Add(this);   
    }

    void OnDestroy()
    {
        allIDHolders.Remove(this);
    }

    public static GameObject GetGameObjectWithID(int ID)
    {
        for (int idx = allIDHolders.Count - 1; idx >= 0; idx--)
        {
            IDHolder i = allIDHolders[idx];
            if (i == null)
            {
                allIDHolders.RemoveAt(idx);
                continue;
            }

            if (i.UniqueID == ID)
                return i.gameObject;
        }
        return null;
    }

    public static void ClearIDHoldersList()
    {
        allIDHolders.Clear();
    }
}
