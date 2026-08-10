using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public static class IDFactory {

    private static int Count;
    private static int LocalOnlyCount = -1000000;

    public static int GetUniqueID()
    {
        // Count++ has to go first, otherwise - unreachable code.
        Count++;
        return Count;
    }

    // IDs négatifs garantis uniques, réservés aux objets purement visuels/locaux
    // (ex: ghost de déplacement en attente) qui ne doivent jamais entrer en collision
    // avec un ID réel distribué par le serveur (toujours positif).
    public static int GetLocalOnlyID()
    {
        LocalOnlyCount--;
        return LocalOnlyCount;
    }
	
	 public static void ResetIDs()
    {
        Count = 0;
        LocalOnlyCount = -1000000;
    }


}
