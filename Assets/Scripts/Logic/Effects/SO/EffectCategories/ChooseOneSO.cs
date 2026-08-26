using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/ChooseOneSO")]
public class ChooseOneSO : EffectSO
{
    [Header("Parameters")]
    public List<CardAsset> CardPool;
    public int ChooseBetweenCount = 2;
    public override EffectPriority Priority => EffectPriority.ChooseOne;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData)
    {
        if (context.Caster == null || CardPool == null || CardPool.Count == 0)
        {
            Log($"{EffectName}: pas de caster ou pool vide, annulé.");
            return;
        }

        int offerCount = Mathf.Clamp(ChooseBetweenCount, 1, CardPool.Count);

        if (NetworkSessionData.IsNetworkSession)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            int playerIndex = context.Caster.playerIndex;
            int sourceEntityID = context.Source is CreatureLogic c ? c.UniqueCreatureID
                : context.Source is BuildingLogic b ? b.UniqueBuildingID : -1;
            int effectIndex = -1;
            if (context.Source is CreatureLogic sc && sc.ca?.Effects != null)
                effectIndex = sc.ca.Effects.FindIndex(e => e.Effect == this);
            else if (context.Source is BuildingLogic sb && sb.ca?.Effects != null)
                effectIndex = sb.ca.Effects.FindIndex(e => e.Effect == this);

            if (sourceEntityID == -1 || effectIndex == -1)
            {
                Log($"{EffectName}: impossible de résoudre sourceEntityID/effectIndex (carte non-créature/bâtiment ?), annulé.");
                return;
            }

            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            GameNetworkManager.QueueOrRunAfterReveal(() =>
                GameNetworkManager.Instance.BroadCastChooseOneOffer(playerIndex, sourceEntityID, effectIndex, seed));
        }
        else
        {
            System.Random rng = new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            List<CardAsset> offer = PickDistinct(CardPool, offerCount, rng);
            ChooseOneManager.Instance.BeginOffer(context.Caster, offer, -1, -1);
        }
    }

    // Tirage sans remise déterministe (Fisher-Yates partiel) — même logique utilisée en local
    // (RNG frais) et en réseau (RNG seedé côté client, voir GameNetworkManager.ChooseOneOfferClientRpc).
    internal static List<CardAsset> PickDistinct(List<CardAsset> pool, int count, System.Random rng)
    {
        List<CardAsset> copy = new List<CardAsset>(pool);
        int n = Mathf.Min(count, copy.Count);
        for (int i = 0; i < n; i++)
        {
            int j = rng.Next(i, copy.Count);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy.GetRange(0, n);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        $"Choisissez 1 carte parmi {ChooseBetweenCount}";
}
