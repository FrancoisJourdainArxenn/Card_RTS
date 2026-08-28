using System.Linq;
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

            for (int i = 0; i < TokenCount; i++)
            {
                switch (Placement)
                {
                    case TokenPlacement.ToHand:
                        GameNetworkManager.QueueOrRunAfterReveal(() =>
                            GameNetworkManager.Instance.BroadCastTokenToHand(playerIndex, sourceEntityID, effectIndex));
                        break;
                    case TokenPlacement.ToZone:
                        // Créée tout de suite, en autorité serveur — au lieu d'attendre l'aller-retour
                        // du ClientRpc — pour que le token puisse rejoindre la file d'attaque de la
                        // bataille en cours de planification (voir SpawnToZone →
                        // ZoneCombatResolver.NotifyCreatureAddedDuringPlanning). Les IDs sont générés
                        // ici pour être diffusés tels quels aux autres clients ; ceux-ci ne recréent
                        // PAS la créature s'ils sont le serveur (voir TokenToZoneClientRpc).
                        int cardID     = IDFactory.GetUniqueID();
                        int creatureID = IDFactory.GetUniqueID();
                        CreatureLogic spawned = SpawnToZone(context, visualData, out int tablePos, creatureID, cardID);
                        if (spawned != null)
                        {
                            // La clé de report active dépend du trigger d'origine (ID de créature pour
                            // OnDeath, zoneDeferKey pour OnBattleStart...) — voir Command.CurrentDeferSourceID.
                            // int.MinValue sert de sentinelle "pas de report" (aucune clé réelle n'y arrive :
                            // ni un UniqueCreatureID, toujours positif, ni un zoneDeferKey, toujours proche de
                            // -2 000 000 000 mais jamais égal à int.MinValue).
                            int deferKey = Command.CurrentDeferSourceID ?? int.MinValue;

                            // Pendant la résolution d'un trigger prédit (OnDeath/OnAttack — voir
                            // ZoneCombatResolver.IsResolvingPredictedTrigger), NE PAS diffuser tout de suite :
                            // ce ClientRpc arriverait, pour TOUS les tokens de TOUTE la bataille, avant l'unique
                            // ApplyCanonicalBattleAssignmentClientRpc envoyé une fois la planification terminée —
                            // un effet à ciblage aléatoire plus tard dans la même bataille verrait alors ce token
                            // côté client alors qu'il n'existait pas encore côté hôte au même point chronologique
                            // (désync constaté sur Flag-Bearer "Inspire" + un Zergling Token de Broodling). On
                            // enregistre à la place ce spawn, tagué par (sourceEntityID, effectIndex) — rejoué au
                            // bon moment relatif dans la boucle de ApplyCanonicalBattleAssignmentClientRpc.
                            if (ZoneCombatResolver.IsResolvingPredictedTrigger)
                                ZoneCombatResolver.RecordPredictedTokenSpawn(sourceEntityID, effectIndex, playerIndex, cardID, creatureID, tablePos, spawned.BaseID, deferKey);
                            else
                                GameNetworkManager.QueueOrRunAfterReveal(() =>
                                    GameNetworkManager.Instance.BroadCastTokenToZone(playerIndex, sourceEntityID, effectIndex, tablePos, spawned.BaseID, cardID, creatureID, deferKey));
                        }
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
                        SpawnToZone(context, visualData, out _);
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

    // creatureID/cardID : -1 = génère un nouvel ID local (solo). En session réseau (appelé depuis
    // Execute, côté serveur), des IDs pré-alloués sont passés pour être diffusés tels quels aux
    // autres clients. Retourne la créature créée (ou null si la zone est pleine) ; tablePos est
    // la position row-locale utilisée, à renvoyer telle quelle dans la diffusion réseau.
    private CreatureLogic SpawnToZone(EffectContext context, EffectVisualData visualData, out int tablePos, int creatureID = -1, int cardID = -1)
    {
        Player caster = context.Caster;
        PlayerArea targetArea = ResolveTargetArea(context);

        bool tokenIsMelee = TokenToSummon.melee;
        CenteredSlots rowSlots = tokenIsMelee && targetArea.tableVisual.meleeSlots != null
            ? targetArea.tableVisual.meleeSlots
            : targetArea.tableVisual.rangedSlots;

        // Compte logique (caster.playedCards.Creatures), pas visuel (tableVisual) : mis à jour de
        // façon synchrone dès qu'un token/créature est inséré, quel que soit le trigger qui l'a
        // créé (OnDeath, OnBattleStart, ETB...) — contrairement au visuel, dont l'affichage peut
        // être différé pendant une résolution anticipée de combat (Command.DeferForBattleReplay).
        // Plusieurs triggers résolus l'un après l'autre dans la même passe de planification (ex :
        // Broodling qui meurt PUIS Queen qui déclenche son OnBattleStart) se voient donc l'un
        // l'autre sans qu'aucun site d'appel n'ait à s'en soucier.
        bool InRow(CreatureLogic cr) => cr.BaseID == targetArea.baseID && cr.IsMelee == tokenIsMelee;
        int currentCount = caster.playedCards.Creatures.Count(InRow);

        // Une créature de CETTE rangée déjà vouée à mourir pendant la bataille en cours (marquée
        // IsPendingDeath, ou dégâts en attente >= vie restante — voir ZoneCombatResolver.WouldSurvive)
        // occupe encore logiquement sa place ici : son retrait de playedCards.Creatures n'a lieu que
        // bien plus tard (CreatureLogic.Die, via ProcessPendingDeaths en fin de Battle). Sans ce
        // filtre, la capacité resterait figée à l'état "plein" pour le reste du combat dès qu'une
        // première créature meurt, même si d'autres meurent ensuite et libèrent d'autres places.
        int effectiveCount = caster.playedCards.Creatures.Count(cr => InRow(cr) && ZoneCombatResolver.WouldSurvive(cr) && !cr.IsPendingMove);

        if (effectiveCount >= GlobalSettings.Instance.MaxCreaturePerRow)
        {
            Debug.LogWarning($"[TokenGenerationSO] Zone pleine ({currentCount}/{GlobalSettings.Instance.MaxCreaturePerRow}), token annulé.");
            new ShowMessageCommand("Zone is full, token could not be spawned.", 2f).AddToQueue();
            tablePos = -1;
            return null;
        }

        CardLogic tokenCard = new CardLogic(TokenToSummon, cardID);
        tokenCard.owner = caster;

        CreatureLogic newCreature = new CreatureLogic(caster, TokenToSummon, targetArea.baseID, creatureID);
        // Capturé tout de suite, avant qu'aucun dégât de CE combat ne soit appliqué : EnqueueBattleCommands
        // mute déjà toutes les Health de la bataille de façon synchrone avant que PlayACreatureCommand
        // (mis en file plus bas) ne s'exécute réellement — lire cl.Health à ce moment-là donnerait la
        // vie de FIN de combat plutôt que la vie de spawn (voir TableVisual.CreateCreatureGO).
        int spawnAttack = newCreature.Attack;
        int spawnHealth = newCreature.Health;
        tablePos         = currentCount; // position row-locale : on ajoute à la fin de la rangée
        int logicalIndex = caster.GetLogicalInsertIndex(TokenToSummon.melee, targetArea.baseID, tablePos);
        caster.playedCards.Creatures.Insert(logicalIndex, newCreature);
        FogOfWarManager.Refresh();
        ZoneCombatResolver.NotifyCreatureAddedDuringPlanning(newCreature);

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

        new PlayACreatureCommand(tokenCard, caster, tablePos, newCreature.UniqueCreatureID, targetArea, spawnAttack, spawnHealth).AddToQueue();

        EffectRegistry.ETB(TokenToSummon, new EffectContext
        {
            Caster = caster,
            Source = newCreature
        });

        EffectRegistry.NotifyTokenCreated(caster, newCreature);

        return newCreature;
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription() =>
        TokenToSummon == null
            ? "Crée un token"
            : $"Crée {TokenCount}x {TokenToSummon.name}";
}
