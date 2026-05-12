using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class EffectSO : ScriptableObject
{
    public string Description = "";
    [SerializeField] protected EffectVisualData visualData;
    protected int _sourceID = -1; //A retirer si ça fou la merde 

    
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
        EffectParameters parameters
    );
    
    public List<IIdentifiable> GetAffectedElements(EffectContext context, EffectInfo effectInfo)
    {
        List<IIdentifiable> eligibleAffectedElements = new();
        foreach (EffectTargetInfo targetInfo in effectInfo.effectTargets)
            eligibleAffectedElements.AddRange(context.GetExecutionAffectedElements(targetInfo));

        List<IIdentifiable> affectedElements = new();

        if (eligibleAffectedElements.Count == 0)
        {
            bool targetTypeIsNone = effectInfo.effectTargets.Count == 1
                && effectInfo.effectTargets[0].targetType == EffectObjectType.None;
            if (!targetTypeIsNone)
                return affectedElements;
            return context.GetSingleTargetAffectedElements(
                null, effectInfo.affectedElements
            );
        }

        foreach (IIdentifiable target in eligibleAffectedElements)
            affectedElements.AddRange(
                context.GetSingleTargetAffectedElements(
                    target, effectInfo.affectedElements
                )
            );

        return affectedElements.Distinct().ToList();
    }

    public void ApplyEffect(EffectInfo effectInfo, List<IIdentifiable> affectedElements, EffectParameters parameters)
    {
        switch (effectInfo.repartition)
        {
            case EffectRepartition.Uniform:
                foreach (ILivable target in affectedElements.Cast<ILivable>())
                    ApplyToTarget(target, parameters.Amount);
                break;

            case EffectRepartition.Random:
            {
                List<EffectTarget> repartition = BuildTargets(affectedElements);
                DistributeRandomly(parameters.Amount, repartition, new());
                Log($"Random repartition — {string.Join(", ", repartition.Select(t => string.Join(" : ", t.target.DisplayName, t.amount)))}");
                ApplyAll(repartition);
                break;
            }

            case EffectRepartition.RandomMeleeFirst:
            {
                List<EffectTarget> repartition = BuildTargets(affectedElements);
                List<EffectTarget> meleePool = repartition.Where(dt => dt.target.IsMelee).ToList();
                List<EffectTarget> primaryPool = meleePool.Count > 0 ? meleePool : repartition;
                List<EffectTarget> fallbackPool = meleePool.Count > 0 ? repartition.Except(meleePool).ToList() : new();
                DistributeRandomly(parameters.Amount, primaryPool, fallbackPool);
                Log($"RandomMeleeFirst repartition — {string.Join(", ", repartition.Select(t => string.Join(" : ", t.target.DisplayName, t.amount)))}");
                ApplyAll(repartition);
                break;
            }
        }
    }
    protected abstract void ApplyToTarget(ILivable target, int amount);
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
            int targetIndex = Random.Range(0, currentPool.Count);
            EffectTarget chosen = currentPool[targetIndex];
            chosen.amount += 1;
            if (IsTargetSaturated(chosen))
                currentPool.Remove(chosen);
        }
    }

    void ApplyAll(List<EffectTarget> repartition)
    {
        foreach (EffectTarget dt in repartition)
        {
            if (dt.amount == 0) continue;
            ApplyToTarget(dt.target, dt.amount);
        }
    }

    public virtual string GetDescription(EffectParameters parameters) => Description;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    protected static void Log(string msg) => Debug.Log($"[Effects] {msg}");
}