using UnityEngine;

[CreateAssetMenu(menuName = "Card RTS/Weighted Draw Config")]
public class WeightedDrawConfig : ScriptableObject
{
    [Header("Income")]
    public int   I_min = 8;
    public int   I_max = 15;

    [Header("Tier probabilities — Early (low income)")]
    [Range(0,1)] public float T1_early = 0.70f;
    [Range(0,1)] public float T2_early = 0.25f;
    [Range(0,1)] public float T3_early = 0.05f;

    [Header("Tier probabilities — Late (high income)")]
    [Range(0,1)] public float T1_late  = 0.20f;
    [Range(0,1)] public float T2_late  = 0.40f;
    [Range(0,1)] public float T3_late  = 0.40f;

    [Header("Re-draw depression")]
    public float reDrawMultiplicator = 1f;
}
