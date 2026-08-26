using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class EffectSO : ScriptableObject
{
    public string Description = "";
    public EffectVisualData EffectVisual;
    protected int _sourceID = -1;
    virtual public EffectPriority Priority => EffectPriority.DrawCards;
    protected virtual int Amount => 0;
    // Les effets qui sont des buffs "tout ou rien" (bouclier, célérité...) se déclarent ici pour être
    // filtrés hors cible sur une structure. ModifyStatsSO ne s'y déclare pas exprès : le verrou posé
    // sur CreatureLogic.Attack bloque déjà la composante attaque, et on veut laisser passer le +HP.
    protected virtual bool IsBuffEffect => false;
    private static System.Random _networkRng;
    internal static System.Random CurrentNetworkRng => _networkRng;
    internal static void SetNetworkRng(System.Random rng) => _networkRng = rng;
    internal static void ClearNetworkRng() => _networkRng = null;

    // Allocation forcée pour le rejeu réseau (Random / RandomMeleeFirst / RandomSingleTarget) : quand
    // posée, ApplyEffect applique l'effet exactement à cette liste (cible, montant) au lieu de tirer
    // au hasard dans affectedElements — évite qu'un pool légèrement différent côté client (ordre,
    // composition, égalité départagée différemment) fasse retomber le tirage sur un résultat différent
    // de celui choisi par le serveur. null = pas d'allocation forcée (résolution normale, tirage local).
    // amount n'est utilisé que par Random/RandomMeleeFirst (RandomSingleTarget l'ignore : ApplyToTarget
    // est appelé sans montant explicite, comme en résolution normale, pour préserver son comportement
    // — utiliser sa propre Amount).
    private static List<(int id, int amount)> _forcedAllocation;
    internal static void SetForcedAllocation(List<(int id, int amount)> allocation) => _forcedAllocation = allocation;
    internal static void ClearForcedAllocation() => _forcedAllocation = null;

    // Allocation réellement produite par le dernier tirage Random / RandomMeleeFirst / RandomSingleTarget
    // (liste vide si pool vide/rien distribué, ou si cette résolution n'a touché aucune de ces trois
    // répartitions) — lue juste après Execute() par les résolveurs serveur (ResolveBattleStartEffects,
    // ResolvePredictedBattleDeath, ResolvePredictedOnAttack, ResolvePredictedOnTakeDamage,
    // EffectRegistry.FireListenersPredicted) pour être diffusée aux clients, exactement comme le seed.
    // Remise à vide par ResetLastAllocation() avant chaque résolution, pour ne jamais retenir le
    // résultat d'un effet précédent sans rapport.
    internal static List<(int id, int amount)> LastAllocation { get; private set; } = new();
    internal static void ResetLastAllocation() => LastAllocation = new();
    // Restaure une valeur précédemment sauvegardée. Les sites d'appel imbriqués (ResolveOnTakeDamageFromEffect,
    // FireListenersPredicted) sauvegardent LastAllocation avant de le réinitialiser pour leur propre
    // capture, puis le restaurent ici dans leur finally — sinon une réaction imbriquée (ex: Queen
    // "More Zergs" côté OnTakeDamage, déclenchée PENDANT ApplyToTarget d'un Fire Bolt encore en cours)
    // efface silencieusement la capture de l'effet englobant avant qu'il ait pu la lire (bug constaté :
    // Cinder Poet touche Queen avec Fire Bolt, Queen réagit en OnTakeDamage, LastAllocation retombe à
    // vide avant que ResolveBattleStartEffects ne le lise pour Fire Bolt — la cible forcée diffusée au
    // client est vide, Fire Bolt ne s'applique jamais côté client).
    internal static void SetLastAllocation(List<(int id, int amount)> allocation) => LastAllocation = allocation ?? new();
    protected class EffectTarget
    {
        public ILivable target;
        public int amount;

        public EffectTarget(ILivable target)
        {
            this.target = target;
            this.amount = 0;
        }
    }
    public abstract void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    );

    public List<IIdentifiable> GetAffectedElements(EffectContext context, EffectInfo effectInfo)
    {
        List<IIdentifiable> eligibleAffectedElements = new();
        foreach (EffectTargetInfo targetInfo in effectInfo.effectTargets)
            eligibleAffectedElements.AddRange(context.GetExecutionAffectedElements(targetInfo));

        List<IIdentifiable> affectedElements = new();
        List<IIdentifiable> result;

        if (eligibleAffectedElements.Count == 0)
        {
            result = effectInfo.effectTargets.Count == 0
                ? context.GetSingleTargetAffectedElements(
                    null, effectInfo.affectedElements
                )
                : affectedElements;
        }
        else
        {
            foreach (IIdentifiable target in eligibleAffectedElements)
                affectedElements.AddRange(
                    context.GetSingleTargetAffectedElements(
                        target, effectInfo.affectedElements
                    )
                );
            result = affectedElements.Distinct().ToList();
        }

        return IsBuffEffect
            ? result.Where(t => !(t is CreatureLogic c && c.ca.IsStructureUnit)).ToList()
            : result;
    }

    public void ApplyEffect(EffectInfo effectInfo, List<IIdentifiable> affectedElements, EffectVisualData visualData)
    {
        switch (effectInfo.repartition)
        {
            case EffectRepartition.Uniform:
                foreach (ILivable target in affectedElements.Cast<ILivable>())
                {
                    ApplyToTarget(target, visualData);
                    if (this is IRevertable r && r.IsTemporary)
                    {
                        ILivable t = target;
                        TempEffectTracker.Register(t.ID, () => r.Revert(t));
                    }
                }
                break;

            case EffectRepartition.Random:
            {
                List<EffectTarget> repartition = ResolveRepartitionPool(affectedElements,
                    pool => DistributeRandomly(Amount, pool, new()));
                Log($"Random repartition — {string.Join(", ", repartition.Select(t => string.Join(" : ", t.target.DisplayName, t.amount)))}");
                ApplyAll(repartition, visualData);
                break;
            }

            case EffectRepartition.RandomMeleeFirst:
            {
                List<EffectTarget> repartition = ResolveRepartitionPool(affectedElements, pool =>
                {
                    List<EffectTarget> meleePool = pool.Where(dt => dt.target.IsMelee).ToList();
                    List<EffectTarget> primaryPool = meleePool.Count > 0 ? meleePool : pool;
                    List<EffectTarget> fallbackPool = meleePool.Count > 0 ? pool.Except(meleePool).ToList() : new();
                    DistributeRandomly(Amount, primaryPool, fallbackPool);
                });
                Log($"RandomMeleeFirst repartition — {string.Join(", ", repartition.Select(t => string.Join(" : ", t.target.DisplayName, t.amount)))}");
                ApplyAll(repartition, visualData);
                break;
            }

            case EffectRepartition.RandomSingleTarget:
            {
                ILivable target;
                if (_forcedAllocation != null)
                {
                    // Rejeu réseau : ne pas retirer au hasard — appliquer directement à la cible que
                    // le serveur a déjà résolue et diffusée, quel que soit l'état du pool local.
                    if (_forcedAllocation.Count == 0)
                    {
                        Log($"[ApplyEffect] Allocation forcée vide pour RandomSingleTarget — rien à appliquer côté client.");
                        break;
                    }
                    target = PhaseEffectPipeline.ResolveEntityByID(_forcedAllocation[0].id) as ILivable;
                    if (target == null)
                    {
                        Debug.LogWarning($"[ApplyEffect] Cible forcée introuvable (ID:{_forcedAllocation[0].id}) — effet RandomSingleTarget annulé côté client.");
                        break;
                    }
                }
                else
                {
                    if (affectedElements.Count == 0) { LastAllocation = new(); break; }
                    int index = _networkRng != null
                        ? _networkRng.Next(0, affectedElements.Count)
                        : Random.Range(0, affectedElements.Count);
                    target = (ILivable)affectedElements[index];
                    LastAllocation = new List<(int, int)> { (target.ID, 0) };
                }

                ApplyToTarget(target, visualData);
                if (this is IRevertable r && r.IsTemporary)
                {
                    ILivable t = target;
                    TempEffectTracker.Register(t.ID, () => r.Revert(t));
                }
                break;
            }

        }
    }

    // Construit le pool de répartition pour Random/RandomMeleeFirst. En résolution normale (pas de
    // rejeu réseau en cours), construit le pool depuis affectedElements et le fait distribuer par
    // `distribute` (DistributeRandomly, éventuellement précédé d'un tri melee-first) puis capture le
    // résultat dans LastAllocation pour diffusion aux clients. En rejeu réseau (_forcedAllocation
    // posé), ignore affectedElements/distribute et reconstruit directement le pool depuis l'allocation
    // reçue du serveur — aucun tirage local, donc aucun risque de diverger d'un pool client différent.
    private List<EffectTarget> ResolveRepartitionPool(List<IIdentifiable> affectedElements, System.Action<List<EffectTarget>> distribute)
    {
        if (_forcedAllocation != null)
        {
            List<EffectTarget> forced = new();
            foreach ((int id, int amount) in _forcedAllocation)
            {
                ILivable target = PhaseEffectPipeline.ResolveEntityByID(id) as ILivable;
                if (target == null)
                {
                    Debug.LogWarning($"[ApplyEffect] Cible forcée introuvable dans l'allocation (ID:{id}) — entrée ignorée côté client.");
                    continue;
                }
                forced.Add(new EffectTarget(target) { amount = amount });
            }
            return forced;
        }

        List<EffectTarget> repartition = BuildTargets(affectedElements);
        distribute(repartition);
        LastAllocation = repartition.Where(t => t.amount > 0).Select(t => (t.target.ID, t.amount)).ToList();
        return repartition;
    }

    protected abstract void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null);
    protected abstract bool IsTargetSaturated(EffectTarget target);

    List<EffectTarget> BuildTargets(List<IIdentifiable> elements) =>
        elements.Cast<ILivable>().Select(t => new EffectTarget(t)).ToList();

    void DistributeRandomly(int amount, List<EffectTarget> primaryPool, List<EffectTarget> fallbackPool)
    {
        List<EffectTarget> currentPool = new(primaryPool);
        List<EffectTarget> nextPool = new(fallbackPool);
        for (int i = 0; i < amount; i++)
        {
            if (currentPool.Count == 0)
            {
                if (nextPool.Count == 0) break;
                (currentPool, nextPool) = (nextPool, new());
            }
            int targetIndex = _networkRng != null
                ? _networkRng.Next(0, currentPool.Count)
                : Random.Range(0, currentPool.Count);
            EffectTarget chosen = currentPool[targetIndex];
            chosen.amount += 1;
            if (IsTargetSaturated(chosen))
                currentPool.Remove(chosen);
        }
    }

    void ApplyAll(List<EffectTarget> repartition, EffectVisualData visualData)
    {
        foreach (EffectTarget et in repartition)
        {
            if (et.amount == 0) continue;
            ApplyToTarget(et.target, visualData, et.amount);
            if (this is IRevertable r && r.IsTemporary)
            {
                ILivable t = et.target;
                int amt = et.amount;
                TempEffectTracker.Register(t.ID, () => r.Revert(t, amt));
            }
        }
    }

    public virtual string GetDescription() => Description;

    // Valeurs à substituer dans CardAsset.Description (placeholders {0}, {1}...) pour refléter les
    // bonus d'amplificateurs actuels de viewer — null si cet effet n'a pas de valeur substituable.
    public virtual object[] GetDescriptionValues(Player viewer, CardAsset playedCard) => null;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    protected static void Log(string msg) => Debug.Log($"[Effects] {msg}");
}
