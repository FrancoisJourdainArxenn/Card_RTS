using UnityEngine;

public enum CounterSource
{
    // Compteur global déjà tenu par le moteur (Player.matchStats / HeroCountUnlock — le même
    // mécanisme que le déblocage des héros).
    MatchStat,
    // Compte les fois où `condition` est vraie. Compteur local à CETTE instance de créature
    // (cf. CreatureLogic.IncrementConditionCounter) — utile pour des compteurs que le moteur ne
    // tient pas nativement (ex: "morts alliées dans MA zone").
    WrappedCondition,
    // Tier actuel de la home base du joueur (Self/Opponent selon `owner`) — reste valable même si
    // context.Source est une créature ou une base secondaire, dont le CurrentTier propre
    // reste toujours figé à T1. `threshold` s'exprime alors en valeur de CardTier (T1=1..T5=5).
    BaseTier,
}

public enum StatOwner
{
    Self,
    Opponent,
}

// Fusion de ce qui était CondStatThreshold (compteur global vs seuil) et CondEveryN (condition
// enveloppée, déclenchement périodique) : les deux étaient "un compteur comparé à N", seule la
// source du compteur et le mode de comparaison changeaient. Les deux axes sont maintenant des
// champs indépendants, ce qui ouvre aussi 2 combinaisons qui n'existaient pas avant (ex: un
// MatchStat qui se redéclenche tous les N, ou une condition enveloppée avec un seuil fixe non
// répétitif).
[CreateAssetMenu(menuName = "Effects/Conditions/Condition:Counter")]
public class CondCounter : ConditionSO
{
    [Header("Counter Source")]
    public CounterSource source = CounterSource.MatchStat;

    [Header("— si source = MatchStat ou BaseTier")]
    public StatOwner owner = StatOwner.Self;
    public MatchStatType stat;

    [Header("— si source = WrappedCondition")]
    public ConditionSO condition;

    [Header("Comparaison")]
    // false : seuil fixe classique (AtLeast/Exactly/AtMost — comportement de l'ex-CondStatThreshold).
    // true  : le compteur "boucle" tous les N passages — se redéclenche périodiquement au lieu de
    // rester vrai en continu une fois le seuil atteint (comportement de l'ex-CondEveryN). `threshold`
    // joue alors le rôle de N, `comparison` est ignoré.
    public bool resetCount;
    public CountComparison comparison = CountComparison.AtLeast;
    public int threshold = 1;

    public override bool Evaluate(EffectContext context)
    {
        int count;

        if (source == CounterSource.WrappedCondition)
        {
            if (condition != null && !condition.Evaluate(context))
                return false;

            count = context.Source switch
            {
                CreatureLogic c => c.IncrementConditionCounter(this),
                _ => 0
            };
        }
        else if (source == CounterSource.BaseTier)
        {
            Player player = owner == StatOwner.Self ? context.Owner : context.Opponent;
            if (player?.homeBaseLogic == null) return false;
            count = (int)player.homeBaseLogic.CurrentTier;
        }
        else
        {
            Player player = owner == StatOwner.Self ? context.Owner : context.Opponent;
            if (player == null) return false;
            count = player.matchStats.Get(stat);
        }

        if (resetCount)
            return threshold > 0 && count % threshold == 0;

        return comparison switch
        {
            CountComparison.AtLeast => count >= threshold,
            CountComparison.Exactly => count == threshold,
            CountComparison.AtMost  => count <= threshold,
            _ => false,
        };
    }

    // Texte de progression pour l'UI (ex: "(2/4)"), en lecture seule — contrairement à Evaluate(),
    // ne fait jamais avancer le compteur WrappedCondition. Retourne null quand ce compteur ne
    // s'applique pas au contexte donné (ex: WrappedCondition affiché sur une carte encore en main,
    // qui n'a donc pas encore d'instance CreatureLogic vivante).
    public string GetProgressText(CreatureLogic sourceCreature, Player cardOwner)
    {
        int count;

        if (source == CounterSource.WrappedCondition)
        {
            if (sourceCreature == null) return null;
            count = sourceCreature.PeekConditionCounter(this);
        }
        else if (source == CounterSource.BaseTier)
        {
            Player statPlayer = owner == StatOwner.Self ? cardOwner : cardOwner?.otherPlayer;
            if (statPlayer?.homeBaseLogic == null) return null;
            count = (int)statPlayer.homeBaseLogic.CurrentTier;
        }
        else
        {
            Player statPlayer = owner == StatOwner.Self ? cardOwner : cardOwner?.otherPlayer;
            if (statPlayer == null) return null;
            count = statPlayer.matchStats.Get(stat);
        }

        int progress = resetCount
            ? (threshold > 0 ? count % threshold : count)
            : Mathf.Min(count, threshold);
        int remaining = threshold - progress;

        if (!resetCount && remaining <= 0)
            return "<i>(Completed)</i>";

        return $"<i>({remaining} Remaining)</i>";
    }
}
