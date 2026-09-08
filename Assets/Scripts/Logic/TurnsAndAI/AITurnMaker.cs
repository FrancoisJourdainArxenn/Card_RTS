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
        // (termine une capture déjà en cours) et TrySendCreaturesToNeutralBases (engage une nouvelle
        // créature vers une base neutre) tournent donc avant le calcul de la réserve ci-dessous, pour
        // qu'une créature tout juste envoyée ce tour (Move() met à jour sa position logique
        // immédiatement, avant même que le visuel n'arrive) soit déjà protégée — pas seulement une fois
        // arrivée visuellement au tour suivant. TryUpgradeTier ne dépense qu'en tout dernier, avec ce
        // qu'il reste une fois ces engagements pris en compte.
        int basesBuilt = TryBuildEligibleNeutralBases();
        int creaturesSent = TrySendCreaturesToNeutralBases();

        // Plafond de déploiement (temps de paix uniquement — aucune base Under Attack) : Income < 3
        // -> 1 créature max, sinon Tier < 5 -> 2 max, sinon illimité. Under Attack lève tout plafond
        // (posture Défense, pas encore implémentée en détail).
        bool anyBaseUnderAttack = p.controlledBases.Exists(b => b.IsUnderAttack);
        int maxCreatures = int.MaxValue;
        int reserve = int.MaxValue;
        if (!anyBaseUnderAttack)
        {
            int currentTier = p.homeBaseLogic != null ? (int)p.homeBaseLogic.CurrentTier : 1;
            if (p.playerMainIncome < 3) maxCreatures = 1;
            else if (currentTier < 5) maxCreatures = 2;

            // Réserve de capture : si une base neutre est déjà occupée par une des nôtres (arrivée ou
            // tout juste envoyée ce tour) mais pas encore payable, ne pas dépenser plus que ce qui
            // laisserait de quoi la payer au tour suivant une fois l'income encaissé. Partagée entre le
            // déploiement de créatures et l'upgrade de tier, dans cet ordre de priorité.
            reserve = Mathf.Max(0, ComputeNeutralCaptureReserve());

            // Gel total si on est à un pas de pouvoir se permettre la base ET le prochain tier ensemble.
            if (ShouldFreezeAllSpending())
                reserve = 0;
        }

        int creaturesPlayed = 0;
        int spent = 0;
        while (creaturesPlayed < maxCreatures && TryPlayBestCreature(reserve - spent, out int costPlayed))
        {
            creaturesPlayed++;
            spent += costPlayed;
        }

        TryUpgradeTier(reserve - spent);

        Debug.Log($"[AI] Command phase — {creaturesPlayed} créature(s) jouée(s), {basesBuilt} base(s) construite(s), {creaturesSent} créature(s) envoyée(s) capturer une base, {p.MainRessourceAvailable} ressource(s) non dépensée(s).");
        TurnManager.Instance.RegisterEndPhase(p);
    }

    private bool TryPlayBestCreature(int maxSpend, out int costPlayed)
    {
        costPlayed = 0;
        PlayerArea homeArea = p.HomeUnit != null ? p.GetPlayerAreaByID(p.HomeUnit.BaseID) : p.MainPArea;
        if (homeArea == null)
            return false;

        CardLogic bestCard = null;
        float bestScore = float.NegativeInfinity;

        foreach (CardLogic card in p.hand.CardsInHand)
        {
            if (card.ca.MaxHealth <= 0) continue; // sorts : passe C
            if (!card.CanBePlayed) continue;
            if (card.MainCost > maxSpend) continue;
            if (!p.CanPlayCreatureInArea(homeArea, card.ca)) continue;

            bool isMelee = card.ca.melee;
            int pendingReveal = p.PendingRevealCount(homeArea.baseID, isMelee);
            if (!homeArea.tableVisual.RowHasSpace(isMelee, pendingReveal))
                continue;

            float score = (card.ca.Attack + card.ca.MaxHealth) / (float)Mathf.Max(1, card.MainCost);
            if (score > bestScore)
            {
                bestScore = score;
                bestCard = card;
            }
        }

        if (bestCard == null)
            return false;

        bool bestIsMelee = bestCard.ca.melee;
        int rowLocalPos = (bestIsMelee ? homeArea.tableVisual.MeleeCreaturesOnTable.Count : homeArea.tableVisual.RangedCreaturesOnTable.Count)
            + p.PendingRevealCount(homeArea.baseID, bestIsMelee);

        Debug.Log($"[AI] Joue {bestCard.ca.Name} (coût {bestCard.MainCost}, score {bestScore:F2}) en {(bestIsMelee ? "mêlée" : "distance")}.");
        costPlayed = bestCard.MainCost;
        p.PlayACreatureFromHand(bestCard, rowLocalPos, homeArea);
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
        p.PlayACreatureFromHand(hero, rowLocalPos, homeArea);
        return true;
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
    private List<NeutralBaseVisual> GetNeutralBasesByPriority(IEnumerable<NeutralBaseVisual> source)
    {
        ZoneLogic mainZone = p.homeBaseLogic?.Zone;
        return source
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
    // arrivée visuelle au tour suivant. Partagé entre ComputeNeutralCaptureReserve et
    // ShouldFreezeAllSpending ci-dessous.
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

    // Gel total de la dépense (créatures ET upgrade de tier payant) : si Ressources actuelles + Income
    // du prochain tour tombent pile à (coût des bases neutres en attente + coût du prochain tier - 1),
    // l'IA est à un pas de pouvoir se permettre les deux à la fois. Dépenser quoi que ce soit ce tour
    // repousserait ce moment d'un tour de plus, donc on gèle tout pour l'atteindre au plus vite.
    private bool ShouldFreezeAllSpending()
    {
        if (p.homeBaseLogic == null || p.homeBaseLogic.IsMaxTier) return false;

        List<int> pendingCosts = GetPendingNeutralCaptureCosts();
        if (pendingCosts.Count == 0) return false;

        int totalNeeded = pendingCosts.Sum() + p.homeBaseLogic.CurrentUpgradeCost;
        int projected = p.MainRessourceAvailable + p.playerMainIncome;
        return projected == totalNeeded - 1;
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
                if (!creature.CanMove) continue;
                if (IsHoldingUncapturedNeutralBase(creature)) continue;
                PlayerArea fromArea = p.GetPlayerAreaByID(creature.BaseID);
                if (fromArea == null || fromArea.parentZone == nzc.zone) continue;
                if (!fromArea.parentZone.CanReach(nzc.zone, p, creature)) continue;

                int pendingReveal = p.PendingRevealCount(targetArea.baseID, creature.IsMelee);
                if (!targetArea.tableVisual.RowHasSpace(creature.IsMelee, pendingReveal)) continue;

                int tablePos = (creature.IsMelee ? targetArea.tableVisual.MeleeCreaturesOnTable.Count : targetArea.tableVisual.RangedCreaturesOnTable.Count)
                    + pendingReveal;

                Debug.Log($"[AI] Envoie {creature.ca.Name} capturer {nbv.baseAsset.BaseName}.");
                creature.Move(targetArea.baseID, tablePos);
                targetedThisPhase.Add(nzc.zone);
                sent++;
                break;
            }
        }
        return sent;
    }
}
