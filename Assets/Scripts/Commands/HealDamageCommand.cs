using UnityEngine;
using System.Collections;

public class HealDamageCommand : Command {

    private int targetID;
    private int amount;
    private int healthAfter;
    private EffectVisualData visualData;


    public HealDamageCommand(int targetID, int amount, int healthAfter, EffectVisualData visualData = null)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.healthAfter = healthAfter;
        this.visualData = visualData;
    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);
        if (targetID == GlobalSettings.Instance.LowPlayer.PlayerID || targetID == GlobalSettings.Instance.TopPlayer.PlayerID)
        {
            // target is a hero
            target.GetComponent<MainBaseVisual>().HealDamage(amount, healthAfter);
        }
        else if (target != null && target.GetComponent<OneBaseManager>() != null)
        {
            target.GetComponent<OneBaseManager>().HealDamage(amount, healthAfter);
        }
        else if (target != null && target.GetComponent<OneBuildingManager>() != null)
        {
            target.GetComponent<OneBuildingManager>().HealDamage(amount, healthAfter);
        }
        else
        {
            target?.GetComponent<OneCreatureManager>()?.HealDamage(amount, healthAfter);
        }
        target?.GetComponent<VfxManager>()?.Play(visualData, amount);

        CommandExecutionComplete();
    }
}
