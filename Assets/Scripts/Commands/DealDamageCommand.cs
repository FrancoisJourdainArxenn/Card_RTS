using UnityEngine;
using DG.Tweening;
public class DealDamageCommand : Command
{
    private int targetID;
    private int sourceID;
    private int amount;
    private int healthAfter;
    private EffectVisualData visualData;
    private float speedMultiplier;
    private Vector3? originPosition;

    public DealDamageCommand(int targetID, int amount, int healthAfter, int sourceID = -1, EffectVisualData visualData = null, float speedMultiplier = 1f, Vector3? originPosition = null)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.healthAfter = healthAfter;
        this.sourceID = sourceID;
        this.visualData = visualData;
        this.speedMultiplier = speedMultiplier;
        this.originPosition = originPosition;
    }

    public override void StartCommandExecution()
    {
        Debug.Log($"[DealDamageCmd] source={sourceID} → cible={targetID} montant={amount} PVaprès={healthAfter} (effet secondaire, ex: modificateur d'attaque)");
        GameObject target = IDHolder.GetGameObjectWithID(targetID);
        if (target == null) { Debug.LogWarning($"[DealDamageCmd] cible {targetID} introuvable — CommandExecutionComplete forcé"); CommandExecutionComplete(); return; }

        if (visualData != null)
        {
            VfxManager vfxManager = target.GetComponent<VfxManager>();
            if (visualData.travelFromSource && originPosition.HasValue && vfxManager != null)
            {
                vfxManager.PlayProjectile(visualData, amount, originPosition.Value, () =>
                {
                    ApplyDamageVisual(target);
                    CommandExecutionComplete();
                });
                return;
            }

            vfxManager?.Play(visualData, amount);
            ApplyDamageVisual(target);
            CommandExecutionComplete();
            return;
        }

        if (sourceID != -1 && sourceID != targetID)
        {
            GameObject source = IDHolder.GetGameObjectWithID(sourceID);
            CreatureAttackVisual attackVisual = source?.GetComponent<CreatureAttackVisual>();
            if (attackVisual != null)
            {
                CreatureLogic.CreaturesCreatedThisGame.TryGetValue(sourceID, out var sourceLogic);
                int sourceHealth = sourceLogic?.Health ?? 0;
                bool fired = false;
                DOVirtual.DelayedCall(0.2f / speedMultiplier, () =>
                {
                    fired = true;
                    Debug.Log($"[DealDamageCmd] délai écoulé — relais vers CreatureAttackVisual.AttackTarget pour source={sourceID} cible={targetID}");
                    attackVisual.AttackTarget(targetID, amount, 0, sourceHealth, healthAfter, speedMultiplier);
                })
                .SetLink(source)
                .OnKill(() => { if (!fired) { Debug.LogWarning($"[DealDamageCmd] delayed call tué avant déclenchement (source={sourceID} détruite/désactivée ?) — CommandExecutionComplete forcé"); CommandExecutionComplete(); } });
                return;
            }
        }

        ApplyDamageVisual(target);
        CommandExecutionComplete();
    }

    private void ApplyDamageVisual(GameObject target)
    {
        if (target == null) return;

        bool isPlayer = targetID == GlobalSettings.Instance.LowPlayer.PlayerID
                     || targetID == GlobalSettings.Instance.TopPlayer.PlayerID;
        if (isPlayer)
        {
            target.GetComponent<MainBaseVisual>()?.TakeDamage(amount, healthAfter);
            return;
        }
        if (target.GetComponent<OneBaseManager>() != null)
            target.GetComponent<OneBaseManager>().TakeDamage(amount, healthAfter);
        else if (target.GetComponent<OneLivableManager>() != null)
            target.GetComponent<OneLivableManager>().TakeDamage(amount, healthAfter);
    }
}
