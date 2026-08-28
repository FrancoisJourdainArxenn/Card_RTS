using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Effects/AmplifyEffectSO")]
public class AmplifyEffectSO : EffectSO
{
    [Header("Parameters")]
    public EffectCategory AppliesTo;
    public int DamageBonus;
    public int HealBonus;
    public int AttackBonus;
    public int HealthBonus;
    public bool ActionOnly = true;

    public override void Execute(
        string EffectName,
        EffectContext context,
        EffectInfo effectInfo,
        EffectVisualData visualData
    )
    {
        if (context.Caster == null)
        {
            Log($"{EffectName}: no caster, cancelled.");
            return;
        }

        // Bonus permanent : chaque application obtient son propre ID local unique (jamais lié à une
        // entité vivante), pour ne jamais être retiré si la source (créature/bâtiment) meurt ensuite
        // et pour s'additionner à chaque nouvelle application au lieu d'écraser la précédente. Sûr en
        // réseau : ce sourceID n'est calculé et utilisé que côté serveur puis diffusé tel quel via
        // EffectAmplifierClientRpc (voir GameNetworkManager), jamais recalculé côté client.
        int sourceID = IDFactory.GetLocalOnlyID();

        Log($"{EffectName}: {context.Caster.name} gains effect amplifier ({AppliesTo}, sourceID={sourceID})");

        EffectAmplifier amplifier = new EffectAmplifier
        {
            AppliesTo = AppliesTo,
            DamageBonus = DamageBonus,
            HealBonus = HealBonus,
            AttackBonus = AttackBonus,
            HealthBonus = HealthBonus,
            SpellsOnly = ActionOnly,
        };

        if (NetworkSessionData.IsNetworkSession)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            GameNetworkManager.Instance.BroadCastEffectAmplifier(context.Caster.playerIndex, sourceID, amplifier);
        }
        else
        {
            context.Caster.AddEffectAmplifier(sourceID, amplifier);
        }
    }

    protected override void ApplyToTarget(ILivable target, EffectVisualData visualData, int? amount = null) { }
    protected override bool IsTargetSaturated(EffectTarget target) => false;

    public override string GetDescription()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((AppliesTo & EffectCategory.Damage) != 0) parts.Add($"+{DamageBonus} Dégâts");
        if ((AppliesTo & EffectCategory.Heal) != 0) parts.Add($"+{HealBonus} Soin");
        if ((AppliesTo & EffectCategory.StatBonus) != 0) parts.Add($"+{AttackBonus}/+{HealthBonus} sur les bonus de stats");
        string scope = ActionOnly ? "de vos Sorts" : "de vos effets";
        return $"{string.Join(" / ", parts)} {scope}";
    }
}
