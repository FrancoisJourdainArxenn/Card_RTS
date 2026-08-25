using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Session de ciblage jouée AVANT que la carte ne soit committée (ressources dépensées,
/// créature créée, carte retirée de la main) — utilisée quand un effet OnPlay a un
/// EffectTargetInfo avec requiresPlayerSelection=true.
///
/// Contrairement à EffectSelectionController (lié à PhaseEffectPipeline, pour toute une phase
/// de jeu), cette session ne porte que sur LA carte en train d'être jouée et peut être annulée
/// sans aucun effet de bord : rien n'a encore été committé au moment où elle démarre.
/// </summary>
public static class OnPlayTargetingSession
{
    private static List<PendingEffectSelection> _queue = new List<PendingEffectSelection>();
    private static int _cursor;
    private static System.Action<List<PendingEffectSelection>> _onConfirmed;
    private static System.Action _onCancelled;
    private static float _beganAtTime = -1f;

    // Le relâchement de souris qui termine le drag (traité par l'ancien système OnMouseUp) peut,
    // dans la même frame, être aussi interprété par l'EventSystem (nouvel Input System) comme un
    // clic UI sur ce qui se trouve sous la carte (ex: ZoneManager.OnPointerClick sur la zone) —
    // constaté en jeu : ce clic fantôme annulait la session la frame même où elle démarrait. On
    // ignore donc tout clic dans les instants qui suivent Begin(), le temps qu'il soit absorbé.
    private const float ClickGraceSeconds = 0.15f;

    public static bool IsActive => _queue.Count > 0;

    /// <summary>
    /// Calcule, pour les effets OnPlay de cette carte qui requièrent une sélection du joueur,
    /// la liste des cibles éligibles. Un effet sans cible éligible est omis (il s'exécutera sans
    /// cible, comme PhaseEffectPipeline le fait déjà pour les triggers de phase). Retourne une
    /// liste vide si aucune sélection n'est nécessaire : la carte peut être jouée normalement.
    /// </summary>
    public static List<PendingEffectSelection> CollectRequiredSelections(CardAsset ca, Player caster, ZoneLogic dropZone)
    {
        List<PendingEffectSelection> result = new List<PendingEffectSelection>();
        if (ca.Effects == null)
            return result;

        foreach (CardEffectData data in ca.Effects)
        {
            if (data.Trigger != TriggerType.OnPlay || !data.RequiresPlayerInput)
                continue;

            EffectContext ctx = new EffectContext { Caster = caster, TargetedZone = dropZone };

            List<IIdentifiable> eligibleTargets = new List<IIdentifiable>();
            foreach (EffectTargetInfo t in data.Effectinfo.effectTargets.Where(t => t.requiresPlayerSelection))
                eligibleTargets.AddRange(ctx.GetEligibleTargets(t));

            if (eligibleTargets.Count == 0)
                continue;

            result.Add(new PendingEffectSelection
            {
                Data              = data,
                Context           = ctx,
                EligibleTargets   = eligibleTargets,
                SourceEntityID    = -1, // la créature n'existe pas encore
                EffectIndexInCard = ca.Effects.IndexOf(data)
            });
        }

        return result;
    }

    /// <summary>Démarre la session : affiche les highlights et attend les clics du joueur.</summary>
    public static void Begin(
        List<PendingEffectSelection> selections,
        System.Action<List<PendingEffectSelection>> onConfirmed,
        System.Action onCancelled)
    {
        _queue       = selections;
        _cursor      = 0;
        _onConfirmed = onConfirmed;
        _onCancelled = onCancelled;
        _beganAtTime = UnityEngine.Time.unscaledTime;

        ShowCurrentHighlights();
    }

    /// <summary>Annule la session en cours. Sans effet de bord : rien n'a encore été committé.</summary>
    public static void Cancel()
    {
        if (!IsActive)
            return;

        System.Action cancelled = _onCancelled;
        Reset();
        cancelled?.Invoke();
    }

    public static bool OnEntityClicked(IIdentifiable clicked)
    {
        if (!IsActive)
            return false;

        if (UnityEngine.Time.unscaledTime - _beganAtTime < ClickGraceSeconds)
            return false; // clic résiduel du drag qui vient de démarrer la session — pas un vrai choix

        PendingEffectSelection current = _queue[_cursor];
        if (!current.EligibleTargets.Contains(clicked))
        {
            // Clic sur une cible invalide (créature/bâtiment/base/zone non éligible) : comme un drag
            // relâché dans une zone non valide, on annule et la carte retourne en main.
            Cancel();
            return true;
        }

        current.SelectedTarget = clicked;
        TargetingVisualEvents.RaiseTargetingStarted(_queue, _cursor);
        _cursor++;

        if (_cursor < _queue.Count)
        {
            ShowCurrentHighlights();
        }
        else
        {
            List<PendingEffectSelection> resolved = _queue;
            System.Action<List<PendingEffectSelection>> confirmed = _onConfirmed;
            Reset();
            confirmed?.Invoke(resolved);
        }

        return true;
    }

    private static void ShowCurrentHighlights()
    {
        TargetingVisualEvents.RaiseTargetingStarted(_queue, _cursor);
        EffectSelectionController.ApplyEntityHighlights(_queue[_cursor]);
    }

    private static void Reset()
    {
        TargetingVisualEvents.RaiseTargetingEnded();
        // Le ghost servant de SourceEntityID est désormais une vraie entité résolvable (voir
        // DragCreatureOnTable) : TargetedArrowManager peut donc avoir instancié une flèche de
        // confirmation à la sélection. Il ne se nettoie que sur OnEffectsExecuting (jamais
        // OnTargetingEnded, par design pour les effets de phase) — on le déclenche donc ici aussi,
        // que la session se termine par une confirmation ou une annulation.
        TargetingVisualEvents.RaiseEffectsExecuting();
        EffectSelectionController.ClearHighlights();
        _queue = new List<PendingEffectSelection>();
        _cursor = 0;
        _onConfirmed = null;
        _onCancelled = null;
    }
}
