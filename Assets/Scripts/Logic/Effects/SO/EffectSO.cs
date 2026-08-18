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
    internal static void SetNetworkRng(System.Random rng) => _networkRng = rng;
    internal static void ClearNetworkRng() => _networkRng = null;
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
                List<EffectTarget> repartition = BuildTargets(affectedElements);
                DistributeRandomly(Amount, repartition, new());
                Log($"Random repartition — {string.Join(", ", repartition.Select(t => string.Join(" : ", t.target.DisplayName, t.amount)))}");
                ApplyAll(repartition, visualData);
                break;
            }

            case EffectRepartition.RandomMeleeFirst:
            {
                List<EffectTarget> repartition = BuildTargets(affectedElements);
                List<EffectTarget> meleePool = repartition.Where(dt => dt.target.IsMelee).ToList();
                List<EffectTarget> primaryPool = meleePool.Count > 0 ? meleePool : repartition;
                List<EffectTarget> fallbackPool = meleePool.Count > 0 ? repartition.Except(meleePool).ToList() : new();
                DistributeRandomly(Amount, primaryPool, fallbackPool);
                Log($"RandomMeleeFirst repartition — {string.Join(", ", repartition.Select(t => string.Join(" : ", t.target.DisplayName, t.amount)))}");
                ApplyAll(repartition, visualData);
                break;
            }

            case EffectRepartition.RandomSingleTarget:
            {
                if (affectedElements.Count == 0) break;
                int index = _networkRng != null
                    ? _networkRng.Next(0, affectedElements.Count)
                    : Random.Range(0, affectedElements.Count);
                ILivable target = (ILivable)affectedElements[index];
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

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    protected static void Log(string msg) => Debug.Log($"[Effects] {msg}");
}
