using UnityEngine;
using DG.Tweening;
public class DealDamageCommand : Command
{
    private int targetID;
    private int sourceID;
    private int amount;
    private int healthAfter;
    private EffectVisualData visualData;

    public DealDamageCommand(int targetID, int amount, int healthAfter, int sourceID = -1, EffectVisualData visualData = null)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.healthAfter = healthAfter;
        this.sourceID = sourceID;
        this.visualData = visualData;
    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);
        if (target == null) { CommandExecutionComplete(); return; }

        if (visualData != null)
        {
            target.GetComponent<VfxManager>()?.Play(visualData, amount);
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
                DOVirtual.DelayedCall(0.2f, () =>
                {
                    fired = true;
                    attackVisual.AttackTarget(targetID, amount, 0, sourceHealth, healthAfter);
                })
                .SetLink(source)
                .OnKill(() => { if (!fired) CommandExecutionComplete(); });
                return;
            }
        }

        ApplyDamageVisual(target);
        CommandExecutionComplete();
    }

    private void ApplyDamageVisual(GameObject target)
    {
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
