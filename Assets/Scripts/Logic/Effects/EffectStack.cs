using System.Collections.Generic;

/// <summary>
/// Pile des effets en attente pour la phase en cours, par index de joueur.
/// Conteneur de données pur : aucune exécution, aucune UI.
/// </summary>
public static class EffectStack
{
    private static readonly Dictionary<int, List<PendingEffectSelection>> _byPlayer
        = new Dictionary<int, List<PendingEffectSelection>>();

    public static bool IsEmpty => _byPlayer.Count == 0;

    public static void Reset() => _byPlayer.Clear();

    public static void AddFor(int playerIndex, List<PendingEffectSelection> effects)
        => _byPlayer[playerIndex] = effects;

    /// <summary>Effets qui nécessitent un input joueur et ont au moins une cible éligible.</summary>
    public static List<PendingEffectSelection> GetSelectionEffectsFor(int playerIndex)
    {
        if (!_byPlayer.TryGetValue(playerIndex, out List<PendingEffectSelection> effects))
            return new List<PendingEffectSelection>();

        List<PendingEffectSelection> result = new List<PendingEffectSelection>();
        foreach (PendingEffectSelection e in effects)
            if (e.Data.RequiresPlayerInput && e.EligibleTargets.Count > 0)
                result.Add(e);
        return result;
    }

    public static List<PendingEffectSelection> GetAllEffects()
    {
        List<PendingEffectSelection> all = new List<PendingEffectSelection>();

        foreach (List<PendingEffectSelection> list in _byPlayer.Values)
            all.AddRange(list);

        return all;
    }
}
