using System;
using System.Collections.Generic;
using UnityEngine;

public static class WeightedDraw
{
    public static CardAsset Draw(
        List<CardAsset> pool,
        Dictionary<CardAsset, int> timesDrawn,
        int cachedN1, int cachedN2, int cachedN3, int cachedN4, int cachedN5,
        int mainIncome,
        int seed,
        WeightedDrawConfig config,
        string playerTag = "?")
    {
        if (pool == null || pool.Count == 0) return null;
        System.Random rng = new System.Random(seed);

        // Phase 1 — Sélection du tier
        float I = mainIncome;
        float ratio = Mathf.Clamp01((I - config.I_min) / (float)(config.I_max - config.I_min));
        int nTotal = pool.Count;

        float wT1 = Mathf.Lerp(config.T1_early, config.T1_late, ratio) * (cachedN1 / (float)nTotal);
        float wT2 = Mathf.Lerp(config.T2_early, config.T2_late, ratio) * (cachedN2 / (float)nTotal);
        float wT3 = Mathf.Lerp(config.T3_early, config.T3_late, ratio) * (cachedN3 / (float)nTotal);
        float wT4 = Mathf.Lerp(config.T4_early, config.T4_late, ratio) * (cachedN4 / (float)nTotal);
        float wT5 = Mathf.Lerp(config.T5_early, config.T5_late, ratio) * (cachedN5 / (float)nTotal);
        float totalTier = wT1 + wT2 + wT3 + wT4 + wT5;

        float pT1 = wT1 / totalTier * 100f;
        float pT2 = wT2 / totalTier * 100f;
        float pT3 = wT3 / totalTier * 100f;
        float pT4 = wT4 / totalTier * 100f;
        float pT5 = wT5 / totalTier * 100f;

        // Sélection du tier par roulette
        int pickedTier = 1;
        double tierRoll = rng.NextDouble() * totalTier;
        if (tierRoll < wT1)                              pickedTier = 1;
        else if (tierRoll < wT1 + wT2)                    pickedTier = 2;
        else if (tierRoll < wT1 + wT2 + wT3)              pickedTier = 3;
        else if (tierRoll < wT1 + wT2 + wT3 + wT4)        pickedTier = 4;
        else                                              pickedTier = 5;

        // Debug.Log(
        //    $"[WeightedDraw|{playerTag}] seed={seed}\n" +
        //    $"  Config early T1..T5={config.T1_early:F2}/{config.T2_early:F2}/{config.T3_early:F2}/{config.T4_early:F2}/{config.T5_early:F2}  " +
        //    $"late T1..T5={config.T1_late:F2}/{config.T2_late:F2}/{config.T3_late:F2}/{config.T4_late:F2}/{config.T5_late:F2}\n" +
        //    $"  Income: I={mainIncome}={I:F1} | " +
        //    $"ratio={ratio:F2} (I_min={config.I_min} I_max={config.I_max})\n" +
        //    $"  Pool: N1={cachedN1} N2={cachedN2} N3={cachedN3} N4={cachedN4} N5={cachedN5} NTotal={nTotal}\n" +
        //    $"  Poids bruts: T1={wT1:F3} T2={wT2:F3} T3={wT3:F3} T4={wT4:F3} T5={wT5:F3} Σ={totalTier:F3}\n" +
        //    $"  Probabilités: T1={pT1:F1}% T2={pT2:F1}% T3={pT3:F1}% T4={pT4:F1}% T5={pT5:F1}% | roll={tierRoll:F3}/{totalTier:F3} → Tier{pickedTier}");

        // Phase 2 — Candidats du tier sélectionné (ordre stable = ordre de pool)
        List<CardAsset> candidates = new List<CardAsset>();
        foreach (CardAsset card in pool)
            if ((int)card.tier == pickedTier) candidates.Add(card);

        if (candidates.Count == 0)
        {
            candidates = pool;
            //Debug.LogWarning($"[WeightedDraw|{playerTag}] Tier{pickedTier} vide dans le pool — fallback sur tout le pool.");
        }

        // Calcul des poids (dépression des cartes déjà piochées)
        double[] weights = new double[candidates.Count];
        double totalW = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            int drawn = timesDrawn.TryGetValue(candidates[i], out int v) ? v : 0;
            weights[i] = 1.0 / Math.Pow(drawn + 1, config.reDrawMultiplicator);
            totalW += weights[i];
        }

        // Sélection par roulette dans le tier
        double cardRoll = rng.NextDouble() * totalW;
        double cumulative = 0;
        CardAsset result = candidates[candidates.Count - 1];
        for (int i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (cardRoll < cumulative) { result = candidates[i]; break; }
        }

        //StringBuilder sb = new StringBuilder();
        //sb.Append($"[WeightedDraw|{playerTag}] Candidats Tier{pickedTier} (roll={cardRoll:F3}/{totalW:F3}) :\n");
        //for (int i = 0; i < candidates.Count; i++)
        //{
        //    int drawn = timesDrawn.TryGetValue(candidates[i], out int v) ? v : 0;
        //    float pct = (float)(weights[i] / totalW * 100);
        //    string arrow = candidates[i] == result ? " ←" : "";
        //    sb.Append($"  {candidates[i].name} (piochée {drawn}x) poids={weights[i]:F3} ({pct:F1}%){arrow}\n");
        //}
        //Debug.Log(sb.ToString());

        return result;
    }
}
