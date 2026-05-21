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
            bool tokenIsMelee = parameters.TokenToSummon.melee;
            // Position de départ dans la rangée concernée (melee ou ranged)
            int baseTablePos = tokenIsMelee
                ? targetArea.tableVisual.MeleeCreaturesOnTable.Count
                : targetArea.tableVisual.RangedCreaturesOnTable.Count;
            SameDistanceChildren rowSlots = tokenIsMelee && targetArea.tableVisual.meleeSlots != null
                ? targetArea.tableVisual.meleeSlots
                : targetArea.tableVisual.rangedSlots;
            int maxSlots = rowSlots.Children.Length;

            for (int i = 0; i < parameters.Amount; i++)
            {
                switch (parameters.Placement)
                {
                    case TokenPlacement.ToHand:
                        GameNetworkManager.Instance.BroadCastTokenToHand(playerIndex, sourceEntityID, effectIndex);
                        break;
                    case TokenPlacement.ToZone:
                        if (baseTablePos + i >= maxSlots)
                        {
                            Debug.LogWarning($"[TokenGenerationSO] Zone pleine ({baseTablePos + i}/{maxSlots}), token {i + 1} annulé.");
                            new ShowMessageCommand("Zone is full, token could not be spawned.", 2f).AddToQueue();
                            continue;
                        }
                        // On envoie la position row-locale (index dans la rangée melee ou ranged)
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
                        context.Caster.GetACardNotFromDeck(parameters.TokenToSummon, visualData: visualData);
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
        else if (context.Source is BuildingLogic sourceBuilding)
        {
            PlayerArea sourceArea = System.Array.Find(
                context.Caster.PAreas,
                a => a.parentZone?.Logic == sourceBuilding.Zone);
            if (sourceArea != null) target = sourceArea;
        }
        return target;
    }

    private void SpawnToZone(EffectContext context, CardAsset tokenAsset, EffectVisualData visualData)
    {
        Player caster = context.Caster;
        PlayerArea targetArea = ResolveTargetArea(context);

        bool tokenIsMelee = tokenAsset.melee;
        int currentCount  = tokenIsMelee
            ? targetArea.tableVisual.MeleeCreaturesOnTable.Count
            : targetArea.tableVisual.RangedCreaturesOnTable.Count;
        SameDistanceChildren rowSlots = tokenIsMelee && targetArea.tableVisual.meleeSlots != null
            ? targetArea.tableVisual.meleeSlots
            : targetArea.tableVisual.rangedSlots;
        int maxSlots = rowSlots.Children.Length;
        if (currentCount >= maxSlots)
        {
            Debug.LogWarning($"[TokenGenerationSO] Zone pleine ({currentCount}/{maxSlots}), token annulé.");
            new ShowMessageCommand("Zone is full, token could not be spawned.", 2f).AddToQueue();
            return;
        }

        CardLogic tokenCard = new CardLogic(tokenAsset);
        tokenCard.owner = caster;

        CreatureLogic newCreature = new CreatureLogic(caster, tokenAsset, targetArea.baseID);
        int tablePos     = currentCount; // position row-locale : on ajoute à la fin de la rangée
        int logicalIndex = caster.GetLogicalInsertIndex(tokenAsset.melee, targetArea.baseID, tablePos);
        caster.playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

        if (visualData?.vfxPrefab != null)
        {
            int newCount     = currentCount + 1;
            int firstSlot    = (maxSlots - newCount) / 2;
            int lastSlot     = firstSlot + newCount - 1;
            int finalSlot    = Mathf.Clamp(lastSlot - currentCount, 0, maxSlots - 1);
            Vector3 spawnPos = rowSlots.Children[finalSlot].transform.position;
            new SpawnVFXCommand(visualData.vfxPrefab, spawnPos).AddToQueue();
            new DelayCommand(0.9f).AddToQueue();
        }

        new PlayACreatureCommand(tokenCard, caster, tablePos, newCreature.UniqueCreatureID, targetArea).AddToQueue();

        EffectRegistry.ETB(tokenAsset, new EffectContext
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
