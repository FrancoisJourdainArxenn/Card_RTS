using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class AITurnMaker : TurnMaker
{
    [Tooltip("Debug : force l'affichage de la main de l'IA face visible, même en partie contre l'IA.")]
    public bool ShowHandForDebug = false;

    public override void OnCommandPhaseEntered()
    {
        base.OnCommandPhaseEntered();
        StartCoroutine(CoPlayCommandPhase());
    }

    private IEnumerator CoPlayCommandPhase()
    {
        yield return new WaitWhile(() => Command.CardDrawPending() || Command.playingQueue);

        TryPlayHero();

        // Capturer une base neutre prime sur toute autre dépense du tour. TryBuildEligibleNeutralBases
        // (termine une capture déjà en cours) tourne donc avant le calcul de la réserve ci-dessous, pour
        // qu'une créature tout juste envoyée ce tour (Move() met à jour sa position logique
        // immédiatement, avant même que le visuel n'arrive) soit déjà protégée — pas seulement une fois
        // arrivée visuellement au tour suivant. TryUpgradeTier ne dépense qu'en tout dernier, avec ce
        // qu'il reste une fois ces engagements pris en compte.
        int basesBuilt = TryBuildEligibleNeutralBases();

        List<BaseLogic> underAttack = p.controlledBases.Where(b => b.IsUnderAttack).ToList();
        int creaturesPlayed;
        int creaturesMoved = 0;
        int reserve;

        if (underAttack.Count > 0)
        {
            // Posture Défense : aucun plafond, la réserve de capture de base neutre est ignorée — la
            // défense prime toujours sur l'économie ET sur l'expansion/l'avancée (voir plus bas, qui ne
            // tourne que dans la posture Paix pour ne jamais consommer le mouvement d'un défenseur).
            creaturesPlayed = DefendBases(underAttack);
            reserve = int.MaxValue;
        }
        else
        {
            // Expansion / avancée (règles 2+8) : les bases neutres restent toujours des cibles (y
            // compris une ancienne base perdue, détruite ou reconquise par l'adversaire, qui redevient
            // neutre) — dès 2+ bases, l'avancée vers l'ennemi s'ajoute en plus, elle ne remplace plus
            // la recherche de bases neutres.
            creaturesMoved = TrySendCreaturesToNeutralBases();
            if (p.controlledBases.Count > 1)
                creaturesMoved += TryAdvanceTowardEnemyBases();

            // Posture Paix — plafond de déploiement : Income < 3 -> 1 créature max, sinon Tier < 5 -> 2
            // max, sinon illimité.
            int maxCreatures = int.MaxValue;
            int currentTier = p.homeBaseLogic != null ? (int)p.homeBaseLogic.CurrentTier : 1;
            if (p.playerMainIncome < 3) maxCreatures = 1;
            else if (currentTier < 5) maxCreatures = 2;

            // Réserve de capture : si une base neutre est déjà occupée par une des nôtres (arrivée ou
            // tout juste envoyée ce tour) mais pas encore payable, ne pas dépenser plus que ce qui
            // laisserait de quoi la payer au tour suivant une fois l'income encaissé. Partagée entre le
            // déploiement de créatures et l'upgrade de tier, dans cet ordre de priorité.
            reserve = Mathf.Max(0, ComputeNeutralCaptureReserve());

            creaturesPlayed = 0;
            int spent = 0;
            while (creaturesPlayed < maxCreatures && TryPlayBestCreatureAnywhere(reserve - spent, out int costPlayed))
            {
                creaturesPlayed++;
                spent += costPlayed;
            }
            reserve -= spent;
        }

        TryUpgradeTier(reserve);

        Debug.Log($"[AI] Command phase — {creaturesPlayed} créature(s) jouée(s), {basesBuilt} base(s) construite(s), {creaturesMoved} créature(s) déplacée(s), {p.MainRessourceAvailable} ressource(s) non dépensée(s).");
        TurnManager.Instance.RegisterEndPhase(p);
    }

    private bool TryPlayBestCreature(PlayerArea targetArea, int maxSpend, out int costPlayed)
    {
        costPlayed = 0;
        if (targetArea == null)
            return false;

        CardLogic bestCard = null;

        foreach (CardLogic card in p.hand.CardsInHand)
        {
            if (card.ca.MaxHealth <= 0) continue; // sorts : passe C
            if (!card.CanBePlayed) continue;
            if (card.MainCost > maxSpend) continue;
            if (!p.CanPlayCreatureInArea(targetArea, card.ca)) continue;

            bool isMelee = card.ca.melee;
            int pendingReveal = p.PendingRevealCount(targetArea.baseID, isMelee);
            if (!targetArea.tableVisual.RowHasSpace(isMelee, pendingReveal))
                continue;

            if (bestCard == null || IsBetterCard(card.ca, bestCard.ca))
                bestCard = card;
        }

        if (bestCard == null)
            return false;

        bool bestIsMelee = bestCard.ca.melee;
        int rowLocalPos = (bestIsMelee ? targetArea.tableVisual.MeleeCreaturesOnTable.Count : targetArea.tableVisual.RangedCreaturesOnTable.Count)
            + p.PendingRevealCount(targetArea.baseID, bestIsMelee);

        Debug.Log($"[AI] Joue {bestCard.DisplayName} (coût {bestCard.MainCost}, priorité {GetCardPriorityRank(bestCard.ca)}) en {(bestIsMelee ? "mêlée" : "distance")}.");
        costPlayed = bestCard.MainCost;
        PlayCreature(bestCard, rowLocalPos, targetArea);
        return true;
    }

    // Priorité de choix de carte (remplace l'ancien score Attaque+Vie/coût) : catégorie d'abord (Splash
    // Attack > On Attack > Arcing Attack > Cone Attack > Piercing Attack > effet OnDeath > aucune), puis
    // à catégorie égale (ou aucune des deux côtés), Héros > Attaque la plus élevée.
    private bool IsBetterCard(CardAsset candidate, CardAsset currentBest)
    {
        int candidateRank = GetCardPriorityRank(candidate);
        int bestRank = GetCardPriorityRank(currentBest);
        if (candidateRank != bestRank) return candidateRank > bestRank;

        if (candidate.IsHero != currentBest.IsHero) return candidate.IsHero;

        return candidate.Attack > currentBest.Attack;
    }

    private int GetCardPriorityRank(CardAsset ca)
    {
        if (ca.AttackModifiers.Exists(m => m is CircularAttackModifierSO)) return 6; // Splash Attack
        if (ca.Effects.Exists(e => e.Trigger == TriggerType.OnAttack)) return 5;
        if (ca.AttackModifiers.Exists(m => m is LateralAttackModifierSO)) return 4; // Arcing Attack
        if (ca.AttackModifiers.Exists(m => m is ConeAttackModifierSO)) return 3;
        if (ca.AttackModifiers.Exists(m => m is PiercingAttackModifierSO)) return 2;
        if (ca.Effects.Exists(e => e.Trigger == TriggerType.OnDeath)) return 1;
        return 0;
    }

    // Comme TryPlayBestCreature, mais choisit la meilleure paire (carte, base) parmi TOUTES les bases
    // contrôlées (Main Base + bases neutres capturées) plutôt que de se limiter à la home area — sinon
    // l'IA ne joue jamais de renfort dans une base secondaire qui n'est pas elle-même Under Attack.
    private bool TryPlayBestCreatureAnywhere(int maxSpend, out int costPlayed)
    {
        costPlayed = 0;
        CardLogic bestCard = null;
        PlayerArea bestArea = null;

        // .ToList() est indispensable ici : Player.controlledBases vide et reconstruit son cache interne
        // à CHAQUE accès (voir Player.cs), et RowHasSpace ci-dessous déclenche GetMaxCreaturePerRow qui
        // réitère lui-même controlledBases — sans ce snapshot, ce foreach externe se ferait modifier la
        // collection qu'il énumère (InvalidOperationException) dès qu'une base secondaire est testée.
        foreach (BaseLogic baseLogic in p.controlledBases.ToList())
        {
            PlayerArea targetArea = GetAreaForBase(baseLogic);
            if (targetArea == null) continue;

            foreach (CardLogic card in p.hand.CardsInHand)
            {
                if (card.ca.MaxHealth <= 0) continue; // sorts : passe C
                if (!card.CanBePlayed) continue;
                if (card.MainCost > maxSpend) continue;
                if (!p.CanPlayCreatureInArea(targetArea, card.ca)) continue;

                bool isMelee = card.ca.melee;
                int pendingReveal = p.PendingRevealCount(targetArea.baseID, isMelee);
                if (!targetArea.tableVisual.RowHasSpace(isMelee, pendingReveal))
                    continue;

                if (bestCard == null || IsBetterCard(card.ca, bestCard.ca))
                {
                    bestCard = card;
                    bestArea = targetArea;
                }
            }
        }

        if (bestCard == null)
            return false;

        bool bestIsMelee = bestCard.ca.melee;
        int rowLocalPos = (bestIsMelee ? bestArea.tableVisual.MeleeCreaturesOnTable.Count : bestArea.tableVisual.RangedCreaturesOnTable.Count)
            + p.PendingRevealCount(bestArea.baseID, bestIsMelee);

        Debug.Log($"[AI] Joue {bestCard.DisplayName} (coût {bestCard.MainCost}, priorité {GetCardPriorityRank(bestCard.ca)}) en {(bestIsMelee ? "mêlée" : "distance")} dans la base {bestArea.baseID}.");
        costPlayed = bestCard.MainCost;
        PlayCreature(bestCard, rowLocalPos, bestArea);
        return true;
    }

    private void TryUpgradeTier(int maxSpend)
    {
        if (p.homeBaseLogic == null || p.homeBaseLogic.IsMaxTier) return;

        int cost = p.homeBaseLogic.CurrentUpgradeCost;
        if (cost > 0)
        {
            bool anyBaseUnderAttack = p.controlledBases.Exists(b => b.IsUnderAttack);
            if (anyBaseUnderAttack || p.MainRessourceAvailable < cost || cost > maxSpend) return;

            // Une capture de base neutre en attente prime toujours sur le tier : tant que les ressources
            // actuelles ne couvrent pas la base ET le tier ensemble, l'upgrade attend son tour (voir
            // TryBuildEligibleNeutralBases, qui tourne avant et capture dès que possible).
            int pendingCost = GetPendingNeutralCaptureCosts().Sum();
            if (pendingCost > 0 && p.MainRessourceAvailable < pendingCost + cost) return;
        }

        Debug.Log($"[AI] Passe au tier suivant (coût {cost}).");
        p.RequestUpgradeBase();
    }

    private bool TryPlayHero()
    {
        PlayerArea homeArea = p.HomeUnit != null ? p.GetPlayerAreaByID(p.HomeUnit.BaseID) : p.MainPArea;
        if (homeArea == null)
            return false;

        CardLogic hero = p.hand.CardsInHand.Find(cl => cl.ca.IsHero && cl.CanBePlayed && p.CanPlayCreatureInArea(homeArea, cl.ca));
        if (hero == null)
            return false;

        bool isMelee = hero.ca.melee;
        int pendingReveal = p.PendingRevealCount(homeArea.baseID, isMelee);
        if (!homeArea.tableVisual.RowHasSpace(isMelee, pendingReveal))
            return false;

        int rowLocalPos = (isMelee ? homeArea.tableVisual.MeleeCreaturesOnTable.Count : homeArea.tableVisual.RangedCreaturesOnTable.Count)
            + pendingReveal;

        Debug.Log($"[AI] Joue le héros {hero.ca.Name}.");
        PlayCreature(hero, rowLocalPos, homeArea);
        return true;
    }

    // Déplace une créature en respectant le même choix immédiat/différé que le drag humain
    // (DragCreatureActions.CommitPlay) : en session réseau ou avec UseDeferredMovesInSolo actif, le
    // mouvement est mis en file (TurnManager.EnqueueSoloMove) et ne se résout visuellement qu'à la fin
    // de la phase Command (FlushSoloMoveBuffer) — sinon l'IA déplacerait ses unités immédiatement sous
    // les yeux du joueur, contrairement à un vrai adversaire réseau dont les actions ne se révèlent
    // qu'une fois les deux joueurs prêts. IsPendingMove reste vrai après l'appel (jusqu'au flush), donc
    // les appelants doivent aussi filtrer sur IsPendingMove pour ne pas re-proposer deux fois la même
    // créature dans le même tour (CanMove seul ne suffit plus : MovementsLeftThisTurn n'est décrémenté
    // qu'au flush, pas à l'enqueue).
    private void MoveCreature(CreatureLogic creature, int targetBaseID, int tablePos)
    {
        if (NetworkSessionData.IsNetworkSession || (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo))
            TurnManager.Instance.EnqueueSoloMove(creature.UniqueCreatureID, targetBaseID, tablePos);
        else
            creature.Move(targetBaseID, tablePos);
    }

    // Même idée que MoveCreature, pour la pose d'une créature depuis la main : la logique (ressource,
    // main, CreatureLogic, OnPlay) est résolue tout de suite dans les deux cas, seule la RÉVÉLATION
    // visuelle sur la table est mise en file jusqu'au flush de fin de phase Command quand ce choix est
    // actif — sinon le joueur verrait les cartes de l'IA apparaître immédiatement, contrairement à un
    // vrai adversaire réseau dont les poses ne se révèlent qu'une fois les deux joueurs prêts. Voir
    // Player.PlayACreatureFromHandHidden / TurnManager.FlushSoloPlayBuffer.
    private void PlayCreature(CardLogic card, int rowLocalPos, PlayerArea area)
    {
        if (NetworkSessionData.IsNetworkSession || (GlobalSettings.Instance != null && GlobalSettings.Instance.UseDeferredMovesInSolo))
            p.PlayACreatureFromHandHidden(card, rowLocalPos, area);
        else
            p.PlayACreatureFromHand(card, rowLocalPos, area);
    }

    // Posture Défense : traite chaque base Under Attack, Main Base d'abord puis les autres par
    // distance croissante à la Main Base. Pour chacune : joue depuis la main jusqu'au seuil (ou
    // épuisement), puis envoie des renforts depuis d'autres zones si toujours insuffisant.
    private int DefendBases(List<BaseLogic> underAttack)
    {
        ZoneLogic mainZone = p.homeBaseLogic?.Zone;
        List<BaseLogic> ordered = underAttack
            .OrderByDescending(b => b.IsHomeBase)
            .ThenBy(b => GetZoneDistance(mainZone, b.Zone))
            .ToList();

        int totalPlayed = 0;
        foreach (BaseLogic baseLogic in ordered)
            totalPlayed += DefendBase(baseLogic);

        return totalPlayed;
    }

    // BaseLogic.ID est un identifiant réseau (IDFactory), distinct de PlayerArea.baseID — il faut
    // retrouver la zone correspondante pour matcher la bonne PlayerArea (même logique que
    // TrySendCreaturesToNeutralBases pour une base neutre, ou que TryPlayHero pour la Main Base).
    private PlayerArea GetAreaForBase(BaseLogic baseLogic)
    {
        if (baseLogic.IsHomeBase)
            return p.HomeUnit != null ? p.GetPlayerAreaByID(p.HomeUnit.BaseID) : p.MainPArea;

        return System.Array.Find(p.PAreas, pa => pa.parentZone == baseLogic.neutralBaseController.zone);
    }

    private int DefendBase(BaseLogic baseLogic)
    {
        ZoneLogic zone = baseLogic.Zone;
        PlayerArea area = GetAreaForBase(baseLogic);
        if (zone == null || area == null)
        {
            Debug.Log($"[AI][DEBUG] DefendBase abandonne pour {baseLogic.DisplayName} : zone={(zone == null ? "null" : "ok")}, area={(area == null ? "null" : "ok")}.");
            return 0;
        }

        int played = 0;

        // Phase 1 : jouer depuis la main, sans plafond de ressources — la défense prime toujours.
        while (!DefenseThresholdMet(zone) && TryPlayBestCreature(area, int.MaxValue, out _))
            played++;

        // Phase 2 : renforts depuis d'autres zones, une fois la main épuisée ou insuffisante. Peut
        // aussi bien puiser dans une créature qui gardait une base neutre non capturée — la défense
        // prime sur cette réserve-là aussi.
        if (!DefenseThresholdMet(zone))
        {
            foreach (CreatureLogic creature in p.Creatures)
            {
                if (DefenseThresholdMet(zone)) break;
                if (!creature.CanMove || creature.IsPendingMove)
                {
                    Debug.Log($"[AI][DEBUG] {creature.DisplayName} (ID:{creature.UniqueCreatureID}, BaseID:{creature.BaseID}) ignorée pour renfort : CanMove={creature.CanMove}, IsPendingMove={creature.IsPendingMove}.");
                    continue;
                }
                PlayerArea fromArea = p.GetPlayerAreaByID(creature.BaseID);
                if (fromArea == null || fromArea.parentZone == area.parentZone)
                {
                    Debug.Log($"[AI][DEBUG] {creature.DisplayName} ignorée : fromArea={(fromArea == null ? "null" : fromArea.baseID.ToString())}, cible={area.baseID}.");
                    continue;
                }
                if (!fromArea.parentZone.CanReach(area.parentZone, p, creature))
                {
                    Debug.Log($"[AI][DEBUG] {creature.DisplayName} ignorée : CanReach=false ({fromArea.baseID} -> {area.baseID}).");
                    continue;
                }

                bool isMelee = creature.IsMelee;
                int pendingReveal = p.PendingRevealCount(area.baseID, isMelee);
                if (!area.tableVisual.RowHasSpace(isMelee, pendingReveal))
                {
                    Debug.Log($"[AI][DEBUG] {creature.DisplayName} ignorée : plus de place sur la table cible (mêlée={isMelee}).");
                    continue;
                }

                int tablePos = (isMelee ? area.tableVisual.MeleeCreaturesOnTable.Count : area.tableVisual.RangedCreaturesOnTable.Count)
                    + pendingReveal;

                Debug.Log($"[AI] Envoie {creature.DisplayName} défendre {baseLogic.DisplayName}.");
                MoveCreature(creature, area.baseID, tablePos);
                played++;
            }
        }

        return played;
    }

    // Seuil de défense : nombre d'unités alliées dans la zone >= nombre d'unités adverses + 2 — une
    // simple comparaison Attaque-vs-Vie s'arrêtait "juste assez" sur le papier mais laissait trop peu
    // d'unités pour survivre au combat réel.
    private bool DefenseThresholdMet(ZoneLogic zone)
    {
        // + PendingIncomingMoveCountForZone : un renfort déjà envoyé plus tôt dans CETTE même boucle
        // (voir DefendBase) n'a pas encore son BaseID à jour (flush en fin de phase Command), donc
        // c.Zone == zone ne le compte pas — sans cet ajout, la boucle continuerait d'en envoyer
        // d'autres au-delà du seuil réellement atteint.
        int ownCount = p.Creatures.Count(c => c.Zone == zone) + TurnManager.Instance.PendingIncomingMoveCountForZone(zone);
        int enemyCount = p.otherPlayer.Creatures.Count(c => c.Zone == zone);
        return ownCount >= enemyCount + 2;
    }

    // Réplique les conditions de Player.CheckIfCanBuild sans ses effets de bord (ShowMessageCommand,
    // pensés pour un joueur humain) — CheckIfCanBuild elle-même n'est appelée qu'une fois qu'on est
    // déjà sûr que ça va réussir, jamais pour évaluer les options.
    private bool CanAiBuildAt(NeutralBaseVisual nbv)
    {
        if (p.MainRessourceAvailable < nbv.baseAsset.mainRessourceBaseCost) return false;

        bool ownPresence = false;
        foreach (TableVisual table in nbv.neutralBaseController.tables)
        {
            bool occupied = table.MeleeCreaturesOnTable.Count > 0 || table.RangedCreaturesOnTable.Count > 0;
            if (!occupied) continue;
            if (table.tag == p.tag) ownPresence = true;
            else return false; // zone contestée par l'adversaire
        }
        return ownPresence;
    }

    private bool HasEnemyPresence(NeutralZoneController nzc)
    {
        foreach (TableVisual table in nzc.tables)
            if (table.tag != p.tag && (table.MeleeCreaturesOnTable.Count > 0 || table.RangedCreaturesOnTable.Count > 0))
                return true;
        return false;
    }

    private bool HasOwnPresence(NeutralZoneController nzc)
    {
        foreach (TableVisual table in nzc.tables)
            if (table.tag == p.tag && (table.MeleeCreaturesOnTable.Count > 0 || table.RangedCreaturesOnTable.Count > 0))
                return true;
        return false;
    }

    // Une créature déjà postée seule sur une base neutre pas encore capturée garde la position —
    // elle ne doit jamais être détournée vers une autre base neutre par TrySendCreaturesToNeutralBases.
    private bool IsHoldingUncapturedNeutralBase(CreatureLogic creature)
    {
        PlayerArea area = p.GetPlayerAreaByID(creature.BaseID);
        if (area == null) return false;

        foreach (NeutralBaseVisual nbv in NeutralBaseVisual.Registry.Values)
        {
            if (nbv == null || !nbv.gameObject.activeSelf) continue;
            NeutralZoneController nzc = nbv.neutralBaseController;
            if (nzc.zone != area.parentZone) continue;
            return HasOwnPresence(nzc) && !HasEnemyPresence(nzc);
        }
        return false;
    }

    // Priorité de capture de bases neutres : Income le plus élevé d'abord, puis la plus proche de la
    // Main Base (en nombre de zones), puis aléatoire. Partagée entre TryBuildEligibleNeutralBases
    // (ordre de dépense quand les ressources ne permettent pas tout capturer le même tour) et
    // TrySendCreaturesToNeutralBases (ordre de ciblage quand il y a plus de bases éligibles que de
    // créatures disponibles à envoyer).
    // Une base à 0 Income est tout en bas de la liste, en dessous de toute autre base : tant qu'il
    // existe au moins une base candidate à Income > 0, les bases à 0 Income sont carrément exclues —
    // elles ne redeviennent des cibles qu'en dernier recours, quand plus aucune autre n'est disponible.
    private List<NeutralBaseVisual> GetNeutralBasesByPriority(IEnumerable<NeutralBaseVisual> source)
    {
        List<NeutralBaseVisual> all = source.ToList();
        List<NeutralBaseVisual> withIncome = all.Where(nbv => nbv.baseAsset.mainRessourceIncome > 0).ToList();
        List<NeutralBaseVisual> pool = withIncome.Count > 0 ? withIncome : all;

        ZoneLogic mainZone = p.homeBaseLogic?.Zone;
        return pool
            .OrderByDescending(nbv => nbv.baseAsset.mainRessourceIncome)
            .ThenBy(nbv => GetZoneDistance(mainZone, nbv.neutralBaseController.zone.Logic))
            .ThenBy(_ => Random.value)
            .ToList();
    }

    // BFS sur le graphe logique des zones (ZoneLogic.AdjacentPaths) — nombre de zones à traverser
    // entre "from" et "to", int.MaxValue si injoignable. Ne préjuge pas de la capacité de mouvement
    // réelle d'une créature par tour (voir CanReach, limité à une zone adjacente) : sert uniquement
    // de départage pour la priorité de capture ci-dessus.
    private int GetZoneDistance(ZoneLogic from, ZoneLogic to)
    {
        if (from == null || to == null) return int.MaxValue;
        if (from == to) return 0;

        Queue<ZoneLogic> frontier = new Queue<ZoneLogic>();
        HashSet<ZoneLogic> visited = new HashSet<ZoneLogic> { from };
        frontier.Enqueue(from);
        int distance = 0;

        while (frontier.Count > 0)
        {
            distance++;
            int levelSize = frontier.Count;
            for (int i = 0; i < levelSize; i++)
            {
                ZoneLogic current = frontier.Dequeue();
                foreach (ZonePathLogic path in current.AdjacentPaths)
                {
                    ZoneLogic next = path.OtherEnd(current);
                    if (next == to) return distance;
                    if (visited.Add(next))
                        frontier.Enqueue(next);
                }
            }
        }
        return int.MaxValue;
    }

    // Coût des bases neutres actuellement en attente de capture : présence à nous (en position
    // LOGIQUE — CreatureLogic.Zone via BaseID, pas la présence visuelle des tables) sans contestation
    // adverse, mais pas encore payable. Move() met à jour BaseID immédiatement, donc une créature tout
    // juste envoyée ce tour par TrySendCreaturesToNeutralBases est déjà comptée ici, sans attendre son
    // arrivée visuelle au tour suivant. Partagé entre ComputeNeutralCaptureReserve et TryUpgradeTier.
    private List<int> GetPendingNeutralCaptureCosts()
    {
        List<int> costs = new List<int>();
        foreach (NeutralBaseVisual nbv in NeutralBaseVisual.Registry.Values)
        {
            if (nbv == null || !nbv.gameObject.activeSelf) continue;

            ZoneLogic zone = nbv.neutralBaseController.zone.Logic;
            bool ownPresence = p.Creatures.Exists(c => c.Zone == zone);
            if (!ownPresence) continue;
            bool enemyPresence = p.otherPlayer.Creatures.Exists(c => c.Zone == zone);
            if (enemyPresence) continue;

            int cost = nbv.baseAsset.mainRessourceBaseCost;
            if (p.MainRessourceAvailable >= cost) continue; // déjà payable, pas en attente

            costs.Add(cost);
        }
        return costs;
    }

    // Montant maximum que l'IA peut dépenser (unités + upgrade de tier) ce tour sans compromettre une
    // capture de base neutre en attente : on préserve de quoi la payer au tour suivant une fois l'income
    // encaissé. int.MaxValue si aucune capture en attente.
    private int ComputeNeutralCaptureReserve()
    {
        int minAllowed = int.MaxValue;
        foreach (int cost in GetPendingNeutralCaptureCosts())
        {
            int allowed = p.MainRessourceAvailable + p.playerMainIncome - cost;
            if (allowed < minAllowed) minAllowed = allowed;
        }
        return minAllowed;
    }

    private int TryBuildEligibleNeutralBases()
    {
        int built = 0;
        IEnumerable<NeutralBaseVisual> eligible = NeutralBaseVisual.Registry.Values.Where(nbv => nbv != null && nbv.gameObject.activeSelf);
        foreach (NeutralBaseVisual nbv in GetNeutralBasesByPriority(eligible))
        {
            if (!CanAiBuildAt(nbv)) continue;

            Debug.Log($"[AI] Capture la base neutre {nbv.baseAsset.BaseName} (coût {nbv.baseAsset.mainRessourceBaseCost}).");
            p.RequestBuildNeutralBase(nbv.NeutralBaseId);
            built++;
        }
        return built;
    }

    // Envoie au plus une créature par base neutre non contestée et pas déjà occupée par nous —
    // la capture elle-même (TryBuildEligibleNeutralBases) se fera au tour suivant, une fois la
    // créature visuellement arrivée sur place (RowHasSpace/CheckIfCanBuild lisent l'état visuel de
    // la table, pas la position logique, qui elle est déjà à jour dès ce Move()).
    private int TrySendCreaturesToNeutralBases()
    {
        int sent = 0;
        HashSet<ZoneManager> targetedThisPhase = new HashSet<ZoneManager>();

        IEnumerable<NeutralBaseVisual> eligible = NeutralBaseVisual.Registry.Values
            .Where(nbv => nbv != null && nbv.gameObject.activeSelf)
            .Where(nbv => !HasEnemyPresence(nbv.neutralBaseController) && !HasOwnPresence(nbv.neutralBaseController));

        foreach (NeutralBaseVisual nbv in GetNeutralBasesByPriority(eligible))
        {
            NeutralZoneController nzc = nbv.neutralBaseController;
            if (targetedThisPhase.Contains(nzc.zone)) continue;

            PlayerArea targetArea = System.Array.Find(p.PAreas, pa => pa.parentZone == nzc.zone);
            if (targetArea == null) continue;

            foreach (CreatureLogic creature in p.Creatures)
            {
                if (!creature.CanMove || creature.IsPendingMove) continue;
                if (IsHoldingUncapturedNeutralBase(creature)) continue;
                PlayerArea fromArea = p.GetPlayerAreaByID(creature.BaseID);
                if (fromArea == null || fromArea.parentZone == nzc.zone) continue;
                if (!fromArea.parentZone.CanReach(nzc.zone, p, creature)) continue;

                int pendingReveal = p.PendingRevealCount(targetArea.baseID, creature.IsMelee);
                if (!targetArea.tableVisual.RowHasSpace(creature.IsMelee, pendingReveal)) continue;

                int tablePos = (creature.IsMelee ? targetArea.tableVisual.MeleeCreaturesOnTable.Count : targetArea.tableVisual.RangedCreaturesOnTable.Count)
                    + pendingReveal;

                Debug.Log($"[AI] Envoie {creature.DisplayName} capturer {nbv.baseAsset.BaseName}.");
                MoveCreature(creature, targetArea.baseID, tablePos);
                targetedThisPhase.Add(nzc.zone);
                sent++;
                break;
            }
        }
        return sent;
    }

    // Règle 8 : dès que l'IA possède 2+ bases, elle avance aussi vers les bases ennemies, en plus de la
    // recherche de bases neutres (voir CoPlayCommandPhase — les deux sont désormais cumulatives, pas
    // exclusives, pour qu'une ancienne base perdue/redevenue neutre reste toujours reconquérable).
    // Chaque créature mobile calcule la base ennemie la plus proche d'ELLE (pas une cible globale
    // unique), égalité de distance -> Main Base ennemie, puis les créatures visant la MÊME base sont
    // regroupées : le groupe n'avance que si son effectif est >= au nombre de défenseurs déjà présents
    // dans la zone visée — sinon on masse plutôt que d'envoyer les unités au carton une par une. Le
    // mouvement étant limité à une zone adjacente par tour (CanReach), chaque créature n'avance que
    // d'un saut (BFS), jamais d'un chemin complet en un coup.
    private int TryAdvanceTowardEnemyBases()
    {
        // .ToList() : controlledBases reconstruit son cache interne à chaque accès (voir Player.cs) —
        // sans snapshot, cette référence resterait vulnérable si quoi que ce soit d'autre y touche
        // pendant la boucle ci-dessous (même raison que TryPlayBestCreatureAnywhere).
        List<BaseLogic> enemyBases = p.otherPlayer.controlledBases.ToList();
        if (enemyBases.Count == 0) return 0;

        var candidates = p.Creatures
            .Where(c => c.CanMove && !c.IsPendingMove && !IsHoldingUncapturedNeutralBase(c) && c.Zone != null)
            .Select(c => new
            {
                creature = c,
                target = enemyBases.OrderBy(b => GetZoneDistance(c.Zone, b.Zone)).ThenByDescending(b => b.IsHomeBase).FirstOrDefault()
            })
            .Where(x => x.target != null)
            .ToList();

        int moved = 0;
        foreach (var group in candidates.GroupBy(x => x.target))
        {
            BaseLogic targetBase = group.Key;
            int attackerCount = group.Count();
            int defenderCount = p.otherPlayer.Creatures.Count(c => c.Zone == targetBase.Zone);

            if (attackerCount < defenderCount)
            {
                Debug.Log($"[AI] Masse avant d'avancer vers {targetBase.DisplayName} : {attackerCount} disponible(s) contre {defenderCount} défenseur(s).");
                continue;
            }

            foreach (var entry in group)
            {
                CreatureLogic creature = entry.creature;
                ZoneLogic nextHop = GetNextHopToward(creature.Zone, targetBase.Zone);
                if (nextHop == null) continue; // déjà arrivé, ou injoignable

                PlayerArea fromArea = p.GetPlayerAreaByID(creature.BaseID);
                PlayerArea nextArea = System.Array.Find(p.PAreas, pa => pa.parentZone.Logic == nextHop);
                if (fromArea == null || nextArea == null) continue;
                if (!fromArea.parentZone.CanReach(nextArea.parentZone, p, creature)) continue;

                bool isMelee = creature.IsMelee;
                int pendingReveal = p.PendingRevealCount(nextArea.baseID, isMelee);
                if (!nextArea.tableVisual.RowHasSpace(isMelee, pendingReveal)) continue;

                int tablePos = (isMelee ? nextArea.tableVisual.MeleeCreaturesOnTable.Count : nextArea.tableVisual.RangedCreaturesOnTable.Count)
                    + pendingReveal;

                Debug.Log($"[AI] Avance {creature.DisplayName} de la zone {fromArea.baseID} vers la zone {nextArea.baseID} (cible : {targetBase.DisplayName}).");
                MoveCreature(creature, nextArea.baseID, tablePos);
                moved++;
            }
        }
        return moved;
    }

    // BFS depuis "target" pour trouver, parmi les voisins de "from", celui qui rapproche le plus de la
    // cible — donne le prochain saut d'un déplacement multi-tours plutôt qu'un chemin complet, puisque
    // le mouvement réel est limité à une zone adjacente par tour. Retourne null si déjà arrivé ou si
    // aucun voisin de "from" ne réduit la distance (injoignable).
    private ZoneLogic GetNextHopToward(ZoneLogic from, ZoneLogic target)
    {
        if (from == null || target == null || from == target) return null;

        Dictionary<ZoneLogic, int> distFromTarget = new Dictionary<ZoneLogic, int> { { target, 0 } };
        Queue<ZoneLogic> frontier = new Queue<ZoneLogic>();
        frontier.Enqueue(target);

        while (frontier.Count > 0)
        {
            ZoneLogic current = frontier.Dequeue();
            foreach (ZonePathLogic path in current.AdjacentPaths)
            {
                ZoneLogic next = path.OtherEnd(current);
                if (distFromTarget.ContainsKey(next)) continue;
                distFromTarget[next] = distFromTarget[current] + 1;
                frontier.Enqueue(next);
            }
        }

        if (!distFromTarget.TryGetValue(from, out int fromDist)) return null;

        ZoneLogic bestNext = null;
        int bestDist = fromDist;
        foreach (ZonePathLogic path in from.AdjacentPaths)
        {
            ZoneLogic candidate = path.OtherEnd(from);
            if (distFromTarget.TryGetValue(candidate, out int candidateDist) && candidateDist < bestDist)
            {
                bestDist = candidateDist;
                bestNext = candidate;
            }
        }
        return bestNext;
    }
}
