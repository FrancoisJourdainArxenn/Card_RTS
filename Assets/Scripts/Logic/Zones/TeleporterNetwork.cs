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
        if (sourceTeleporter == null || destTeleporter == null)
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
}
