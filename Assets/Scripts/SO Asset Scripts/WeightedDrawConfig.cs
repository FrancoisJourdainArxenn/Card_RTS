using UnityEngine;

[CreateAssetMenu(menuName = "Card RTS/Weighted Draw Config")]
public class WeightedDrawConfig : ScriptableObject
{
    [Header("Income")]
    public int   I_min = 3;
    public int   I_max = 10;

    [Header("Tier probabilities — Early (low income)")]
    [Range(0,1)] public float T1_early = 0.70f;
    [Range(0,1)] public float T2_early = 0.25f;
    [Range(0,1)] public float T3_early = 0f;

    [Header("Tier probabilities — Late (high income)")]
    [Range(0,1)] public float T1_late  = 0.20f;
    [Range(0,1)] public float T2_late  = 0.40f;
    [Range(0,1)] public float T3_late  = 0f;

    [Header("Re-draw depression")]
    public float reDrawMultiplicator = 1f;
}
