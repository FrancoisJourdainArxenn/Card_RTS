using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/TokenGenerationSO")]
public class TokenGenerationSO : EffectSO
{
    [Header("Parameters")]
    public int TokenCount;
    public CardAsset TokenToSummon;
    public TokenPlacement Placement;
    public override EffectPriority Priority => EffectPriority.TokenGeneration;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        if (context.Caster == null || TokenToSummon == null)
        {
            Log($"{EffectName}: missing caster or token asset, cancelled.");
            return;
        }

        Log($"{EffectName}: {context.Caster.name} creates {TokenCount}x {TokenToSummon.name} — {Placement}");

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
            bool tokenIsMelee = TokenToSummon.melee;
            // Position de départ dans la rangée concernée (melee ou ranged)
            int baseTablePos = tokenIsMelee
                ? targetArea.tableVisual.MeleeCreaturesOnTable.Count
                : targetArea.tableVisual.RangedCreaturesOnTable.Count;
            CenteredSlots rowSlots = tokenIsMelee && targetArea.tableVisual.meleeSlots != null
                ? targetArea.tableVisual.meleeSlots
                : targetArea.tableVisual.rangedSlots;
            int maxSlots = rowSlots.MaxCreatures;

            for (int i = 0; i < TokenCount; i++)
            {
                switch (Placement)
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
            for (int i = 0; i < TokenCount; i++)
            {
                switch (Placement)
                {
                    case TokenPlacement.ToHand:
                        context.Caster.GetACardNotFromDeck(TokenToSummon, visualData: visualData);
                        break;
                    case TokenPlacement.ToZone:
                        SpawnToZone(context, visualData);
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

    private void SpawnToZone(EffectContext context, EffectVisualData visualData)
    {
        Player caster = context.Caster;
        PlayerArea targetArea = ResolveTargetArea(context);

        bool tokenIsMelee = TokenToSummon.melee;
        int currentCount  = tokenIsMelee
            ? targetArea.tableVisual.MeleeCreaturesOnTable.Count
            : targetArea.tableVisual.RangedCreaturesOnTable.Count;
        CenteredSlots rowSlots = tokenIsMelee && targetArea.tableVisual.meleeSlots != null
            ? targetArea.tableVisual.meleeSlots
            : targetArea.tableVisual.rangedSlots;
        int maxSlots = rowSlots.MaxCreatures;

        if (currentCount >= maxSlots)
        {
            Debug.LogWarning($"[TokenGenerationSO] Zone pleine ({currentCount}/{maxSlots}), token annulé.");
            new ShowMessageCommand("Zone is full, token could not be spawned.", 2f).AddToQueue();
            return;
        }

        CardLogic tokenCard = new CardLogic(TokenToSummon);
        tokenCard.owner = caster;

        CreatureLogic newCreature = new CreatureLogic(caster, TokenToSummon, targetArea.baseID);
        int tablePos     = currentCount; // position row-locale : on ajoute à la fin de la rangée
        int logicalIndex = caster.GetLogicalInsertIndex(TokenToSummon.melee, targetArea.baseID, tablePos);
        caster.playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();

        if (visualData?.vfxPrefab != null)
        {
            ZoneManager targetZone = targetArea.parentZone;
            bool isVisible = targetZone == null || FogOfWarManager.Instance == null
                             || !FogOfWarManager.Instance.IsZoneFogged(targetZone);

            if (isVisible)
            {
                int newCount     = currentCount + 1;
                Vector3 spawnPos = rowSlots.GetSlotPosition(currentCount, newCount);
                new SpawnVFXCommand(visualData.vfxPrefab, spawnPos).AddToQueue();
                new DelayCommand(0.9f).AddToQueue();
            }
        }

        new PlayACreatureCommand(tokenCard, caster, tablePos, newCreature.UniqueCreatureID, targetArea).AddToQueue();

        EffectRegistry.ETB(TokenToSummon, new EffectContext
        {
            Caster = caster,
            Source = newCreature
        });
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        TokenToSummon == null
            ? "Crée un token"
            : $"Crée {TokenCount}x {TokenToSummon.name}";
}
