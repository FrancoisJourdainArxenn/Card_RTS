using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/DamageThenBuffAttackSO")]
public class DamageThenBuffAttackSO : HealthEffectSO, IRevertable
{
    [Header("Parameters")]
    public int Damage;
    public int AttackBonus;
    public int HealthBonus;

    [Header("Temporary Effect")]
    public bool IsTempEffect;
    public EffectVisualData RevertVisual;

    protected override int Amount => _amplifiedDamage;
    public bool IsTemporary => IsTempEffect;
    public override EffectPriority Priority => EffectPriority.DealDamage;

    private readonly HashSet<int> _buffedTargets = new HashSet<int>();
    private int _amplifiedDamage;
    private int _amplifiedAttackBonus;
    private int _amplifiedHealthBonus;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        Log($"[DamageThenBuffAttack] TRIGGERED — {EffectName} | source: {context.Source?.DisplayName ?? "none"} | damage: {Damage} | atk bonus: {AttackBonus}");
        _sourceID = context.Source?.ID ?? -1;
        _caster = context.Caster;
        _playedCard = context.AmplifierCard;
        (int bonusAttack, int bonusHealth) = _caster != null ? _caster.GetStatBonus(_playedCard) : (0, 0);
        _amplifiedDamage = Damage + (_caster != null ? _caster.GetDamageBonus(_playedCard) : 0);
        _amplifiedAttackBonus = AttackBonus + bonusAttack;
        _amplifiedHealthBonus = HealthBonus + bonusHealth;
        _buffedTargets.Clear();
        base.Execute(EffectName, context, effectInfo, visualData);
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null)
    {
        int dmg = amount ?? _amplifiedDamage;

        // --- Étape 1 : dégâts (mirroir de DealDamageSO) ---
        bool hasCustomVfx = visualData != null && (visualData.vfxPrefab != null || visualData.overlayMaterial != null);
        bool suppressFallback = visualData != null && visualData.suppressAttackFallback;
        int sourceId = (hasCustomVfx || suppressFallback) ? -1 : _sourceID;

        Vector3? originPosition = null;
        if (hasCustomVfx && visualData.travelFromSource)
        {
            GameObject sourceGO = IDHolder.GetGameObjectWithID(_sourceID);
            if (sourceGO != null)
                originPosition = sourceGO.transform.position;
        }

        int healthBefore = target.Health;
        int healthAfter = target.TakeDamage(dmg);
        int effectiveDealt = healthBefore - healthAfter;
        if (effectiveDealt > 0) _caster?.matchStats.Add(MatchStatType.DamageDealt, effectiveDealt);
        DeathDrainRecorder.RecordAnim(sourceId, target.ID, dmg, healthAfter);
        new DealDamageCommand(target.ID, dmg, healthAfter, sourceId, hasCustomVfx ? visualData : null, originPosition: originPosition).AddToQueue();

        // --- Étape 2 : gain de stats, seulement si la cible a survécu ---
        if (healthAfter <= 0)
        {
            Log($"[DamageThenBuffAttack] SKIP BUFF — {target.DisplayName} (ID:{target.ID}) n'a pas survécu.");
            return;
        }

        int attackBefore = target.Attack;
        ApplyStatsDelta(target, _amplifiedAttackBonus, _amplifiedHealthBonus);
        int actualAttackDelta = target.Attack - attackBefore;
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, _amplifiedHealthBonus, target.Health, EffectVisual).AddToQueue();
        _buffedTargets.Add(target.ID);
    }

    public void Revert(ILivable target, int? _ = null)
    {
        if (!_buffedTargets.Remove(target.ID)) return;

        int attackBefore = target.Attack;
        ApplyStatsDelta(target, -_amplifiedAttackBonus, -_amplifiedHealthBonus);
        int actualAttackDelta = target.Attack - attackBefore;
        new ModifyStatsCommand(target.ID, actualAttackDelta, target.Attack, -_amplifiedHealthBonus, target.Health, RevertVisual).AddToQueue();
    }

    private static void ApplyStatsDelta(ILivable target, int attackDelta, int healthDelta)
    {
        target.Attack += attackDelta;
        target.MaxHealth += healthDelta;
        target.Health += healthDelta;
    }

    protected override bool IsTargetSaturated(EffectTarget target) =>
        target.amount >= target.target.Health;

    public override string GetDescription()
    {
        List<string> parts = new List<string>();
        if (AttackBonus > 0) parts.Add($"+{AttackBonus} Attaque");
        if (HealthBonus > 0) parts.Add($"+{HealthBonus} Vie");
        string bonus = string.Join(" / ", parts);
        return $"Inflige {Damage} dégâts. Si la cible survit, elle gagne {bonus}{(IsTempEffect ? " jusqu'à la fin du tour" : "")}";
    }

    public override object[] GetDescriptionValues(Player viewer, CardAsset playedCard)
    {
        (int bonusAttack, int bonusHealth) = viewer != null ? viewer.GetStatBonus(playedCard) : (0, 0);
        int bonusDamage = viewer != null ? viewer.GetDamageBonus(playedCard) : 0;
        return new object[] { Damage + bonusDamage, AttackBonus + bonusAttack, HealthBonus + bonusHealth };
    }
}
