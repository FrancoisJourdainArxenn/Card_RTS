using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public static class ShufflingExtention {

    // not my code!!!!!
    // got it here: http://stackoverflow.com/questions/273313/randomize-a-listt/1262619#1262619 
    private static System.Random rng = new System.Random();

    public static void Shuffle<T>(this IList<T> list)
    {
        int n = list.Count;
        while (n > 1) {
            n--;
            int k = rng.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    public static void ShuffleWithSeed<T>(this IList<T> list, int seed)
    {
        System.Random seededRng = new System.Random(seed);
        int n = list.Count;
        while (n > 1) {
            n--;
            int k = seededRng.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    public static T SelectRandomCardFromSeed<T>(this IList<T> list, int seed)
    {
        System.Random seededRng = new System.Random(seed);
        int index = seededRng.Next(list.Count);
        return list[index];
    }

    public static T SelectRandomCard<T>(this IList<T> list)
    {
        int index = rng.Next(list.Count);
        return list[index];
    }
}
