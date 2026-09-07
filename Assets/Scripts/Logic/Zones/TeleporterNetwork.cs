// Réseau de téléportation : calculé à la volée depuis les créatures vivantes, jamais persisté.
// Deux zones sont liées tant qu'un téléporteur du joueur en mouvement se trouve dans chacune
// d'elles — avec 3+ téléporteurs, ça forme automatiquement un maillage complet. Pas d'état à
// nettoyer à la mort d'un téléporteur : la zone cesse simplement d'apparaître au prochain appel.
public static class TeleporterNetwork
{
    public static bool CanTraverse(Player player, ZoneManager from, ZoneManager to, CreatureLogic mover)
        => TryGetLink(player, from, to, mover, out _, out _);

    // Variante utilisée par l'affichage (flèche de mouvement en attente) : identique à CanTraverse,
    // mais renvoie aussi les téléporteurs concrets empruntés (départ/arrivée) pour que la flèche
    // puisse faire un crochet par leur position — voir OneCreatureManager.ShowPendingMoveArrowViaTeleporter.
    // S'il existe plusieurs téléporteurs amis dans une même zone, le premier trouvé est utilisé —
    // la légalité du déplacement ne dépend pas duquel, seulement qu'au moins un soit présent.
    public static bool TryGetLink(Player player, ZoneManager from, ZoneManager to, CreatureLogic mover, out CreatureLogic sourceTeleporter, out CreatureLogic destTeleporter)
    {
        sourceTeleporter = null;
        destTeleporter = null;
        if (from == to) return false;

        sourceTeleporter = FindFriendlyTeleporterIn(player, from.Logic);
        destTeleporter = FindFriendlyTeleporterIn(player, to.Logic);
        bool sourceLinked = sourceTeleporter != null || HasFriendlyBaseTeleporterIn(player, from.Logic);
        bool destLinked = destTeleporter != null || HasFriendlyBaseTeleporterIn(player, to.Logic);
        if (!sourceLinked || !destLinked)
        {
            sourceTeleporter = null;
            destTeleporter = null;
            return false;
        }

        // Même convention que ZonePath.Start() (tri par position Z) : garde une notion de
        // "forward" cohérente avec le reste du plateau pour la règle de blocage normale
        // (ZonePathLogic.CanTraverse — un ennemi dans la zone de départ bloque l'avancée).
        ZoneManager zoneA = from.transform.position.z <= to.transform.position.z ? from : to;
        ZoneManager zoneB = zoneA == from ? to : from;

        ZonePathLogic virtualLink = new ZonePathLogic(0, zoneA.Logic, zoneB.Logic);
        if (!virtualLink.CanTraverse(player, from.Logic, mover.IsFlying))
        {
            sourceTeleporter = null;
            destTeleporter = null;
            return false;
        }
        return true;
    }

    private static CreatureLogic FindFriendlyTeleporterIn(Player player, ZoneLogic zone)
    {
        foreach (CreatureLogic c in player.playedCards.Creatures)
            if (c.IsTeleporter && c.Zone == zone)
                return c;
        return null;
    }

    // Une base-téléporteur compte pour la légalité du lien mais ne fournit pas de point d'ancrage
    // visuel (OneBaseManager n'a pas d'équivalent à OneCreatureManager.CenterPointPosition) —
    // ShowPendingMoveArrowViaTeleporter reste purement créature-à-créature pour ce tronçon.
    // Le Téléporteur d'une base est une capacité de faction (voir Terrans Base.asset) : elle vaut pour
    // TOUTE base contrôlée par le joueur, capturée ou non — le palier de tier se lit sur la home base,
    // une base secondaire ne montant jamais elle-même de tier (CurrentTier y reste figé à T1).
    private static bool HasFriendlyBaseTeleporterIn(Player player, ZoneLogic zone)
    {
        foreach (BaseLogic b in player.controlledBases)
        {
            if (b.Zone != zone) continue;
            bool isTeleporter = b.ba.IsTeleporter || player.baseAsset.IsTeleporter;
            if (!isTeleporter) continue;
            CardTier minTier = b.ba.IsTeleporter ? b.ba.teleporterMinTier : player.baseAsset.teleporterMinTier;
            if (player.homeBaseLogic != null && player.homeBaseLogic.CurrentTier >= minTier)
                return true;
        }
        return false;
    }
}
