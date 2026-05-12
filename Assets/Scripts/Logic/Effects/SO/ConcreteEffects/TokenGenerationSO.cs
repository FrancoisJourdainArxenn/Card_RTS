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

    private void SpawnToZone(EffectContext context, CardAsset tokenAsset, EffectVisualData visualData)
    {
        Player caster = context.Caster;

        // Spawn in the same zone as the source creature, fallback to MainPArea
        PlayerArea targetArea = caster.MainPArea;
        if (context.Source is CreatureLogic sourceCreature)
        {
            PlayerArea sourceArea = caster.GetPlayerAreaByID(sourceCreature.BaseID);
            if (sourceArea != null) targetArea = sourceArea;
        }

        // CardLogic temporaire — jamais ajouté en main, sert uniquement à transmettre
        // le CardAsset à PlayACreatureCommand pour le spawn visuel
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

        caster.HighlightPlayableCards();
    }

    protected override void ApplyToTarget(ILivable target, int amount, EffectVisualData visualData) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription(EffectParameters parameters) =>
        parameters.TokenToSummon == null
            ? "Crée un token"
            : $"Crée {parameters.Amount}x {parameters.TokenToSummon.name}";
}
