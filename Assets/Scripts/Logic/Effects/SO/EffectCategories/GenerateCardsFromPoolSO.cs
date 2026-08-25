using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/GenerateCardsFromPoolSO")]
public class GenerateCardsFromPoolSO : EffectSO
{
    [Header("Parameters")]
    public List<CardAsset> CardPool;
    public int CardCount = 1;
    public override EffectPriority Priority => EffectPriority.TokenGeneration;

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

        Log($"{EffectName}: {context.Caster.name} génère {CardCount} carte(s) depuis un pool de {CardPool.Count}.");

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

            // Un seed + un ClientRpc par carte à générer — même principe que TokenGenerationSO.BroadCastTokenToHand,
            // mais avec un pick aléatoire (seedé) dans le pool au lieu d'un TokenToSummon fixe.
            for (int i = 0; i < CardCount; i++)
            {
                int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                GameNetworkManager.Instance.BroadCastPoolCardToHand(playerIndex, sourceEntityID, effectIndex, seed);
            }
        }
        else
        {
            for (int i = 0; i < CardCount; i++)
            {
                CardAsset picked = CardPool[UnityEngine.Random.Range(0, CardPool.Count)];
                context.Caster.GetACardNotFromDeck(picked, visualData: visualData);
            }
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        $"Générez {CardCount} carte(s) aléatoire(s) parmi {(CardPool != null ? CardPool.Count : 0)}";
}
