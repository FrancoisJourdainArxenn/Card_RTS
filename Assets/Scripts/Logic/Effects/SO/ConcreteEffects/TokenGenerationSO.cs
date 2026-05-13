using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/TokenGenerationSO")]
public class TokenGenerationSO : EffectSO
{
    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectParameters parameters,
        EffectVisualData visualData
    )
    {
        if (context.Caster == null || parameters.TokenToSummon == null)
        {
            Log($"{EffectName}: missing caster or token asset, cancelled.");
            return;
        }

        Log($"{EffectName}: {context.Caster.name} creates {parameters.Amount}x {parameters.TokenToSummon.name} — {parameters.Placement}");

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
                Log($"{EffectName}: impossible de résoudre sourceEntityID/effectIndex, annulé.");
                return;
            }

            PlayerArea targetArea = ResolveTargetArea(context);
            int baseTablePos = context.Caster.playedCards.Creatures.Count;

            for (int i = 0; i < parameters.Amount; i++)
            {
                switch (parameters.Placement)
                {
                    case TokenPlacement.ToHand:
                        GameNetworkManager.Instance.BroadCastTokenToHand(playerIndex, sourceEntityID, effectIndex);
                        break;
                    case TokenPlacement.ToZone:
                        GameNetworkManager.Instance.BroadCastTokenToZone(playerIndex, sourceEntityID, effectIndex, baseTablePos + i, targetArea.baseID);
                        break;
                }
            }
        }
        else
        {
            for (int i = 0; i < parameters.Amount; i++)
            {
                switch (parameters.Placement)
                {
                    case TokenPlacement.ToHand:
                        context.Caster.GetACardNotFromDeck(parameters.TokenToSummon);
                        break;
                    case TokenPlacement.ToZone:
                        SpawnToZone(context, parameters.TokenToSummon, visualData);
                        break;
                }
            }
        }
    }

    private static PlayerArea ResolveTargetArea(EffectContext context)
    {
        PlayerArea target = context.Caster.MainPArea;
        if (context.Source is CreatureLogic sourceCreature)
        {
            PlayerArea sourceArea = context.Caster.GetPlayerAreaByID(sourceCreature.BaseID);
            if (sourceArea != null) target = sourceArea;
        }
        return target;
    }

    private void SpawnToZone(EffectContext context, CardAsset tokenAsset, EffectVisualData visualData)
    {
        Player caster = context.Caster;
        PlayerArea targetArea = ResolveTargetArea(context);

        CardLogic tokenCard = new CardLogic(tokenAsset);
        tokenCard.owner = caster;

        CreatureLogic newCreature = new CreatureLogic(caster, tokenAsset, targetArea.baseID);
        int tablePos = caster.playedCards.Creatures.Count;
        caster.playedCards.Creatures.Insert(tablePos, newCreature);
        FogOfWarManager.Refresh();

        new PlayACreatureCommand(tokenCard, caster, tablePos, newCreature.UniqueCreatureID, targetArea).AddToQueue();

        EffectProcessor.ETB(tokenAsset, new EffectContext
        {
            Caster = caster,
            Source = newCreature
        });
    }

    protected override void ApplyToTarget(ILivable target, int amount, EffectVisualData visualData) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription(EffectParameters parameters) =>
        parameters.TokenToSummon == null
            ? "Crée un token"
            : $"Crée {parameters.Amount}x {parameters.TokenToSummon.name}";
}
