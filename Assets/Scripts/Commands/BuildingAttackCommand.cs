using UnityEngine;

public class BuildingAttackCommand : Command
{
    private int targetID;
    private int attackerID;
    private int damageTakenByTarget;
    private int damageTakenByAttacker;
    private int attackerHealthAfter;
    private int targetHealthAfter;

    public BuildingAttackCommand(int targetID, int attackerID, int damageTakenByAttacker, int damageTakenByTarget, int attackerHealthAfter, int targetHealthAfter)
    {
        this.targetID = targetID;
        this.attackerID = attackerID;
        this.damageTakenByAttacker = damageTakenByAttacker;
        this.damageTakenByTarget = damageTakenByTarget;
        this.attackerHealthAfter = attackerHealthAfter;
        this.targetHealthAfter = targetHealthAfter;
    }

    public override void StartCommandExecution()
    {
        GameObject attackerGO = IDHolder.GetGameObjectWithID(attackerID);
        GameObject targetGO = IDHolder.GetGameObjectWithID(targetID);

        if (attackerGO != null)
        {
            if (damageTakenByAttacker > 0)
                DamageEffect.CreateDamageEffect(attackerGO.transform.position, damageTakenByAttacker);
            OneBuildingManager attackerManager = attackerGO.GetComponent<OneBuildingManager>();
            if (attackerManager != null)
                attackerManager.HealthText.text = attackerHealthAfter.ToString();
        }

        if (targetGO != null)
        {
            if (damageTakenByTarget > 0)
                DamageEffect.CreateDamageEffect(targetGO.transform.position, damageTakenByTarget);

            if (BuildingLogic.BuildingsCreatedThisGame.ContainsKey(targetID))
                targetGO.GetComponent<OneBuildingManager>().HealthText.text = targetHealthAfter.ToString();
            else if (CreatureLogic.CreaturesCreatedThisGame.ContainsKey(targetID))
                targetGO.GetComponent<OneCreatureManager>().HealthText.text = targetHealthAfter.ToString();
            else if (BaseLogic.BasesCreatedThisGame.ContainsKey(targetID))
                targetGO.GetComponent<OneBaseManager>().HealthText.text = targetHealthAfter.ToString();
        }

        CommandExecutionComplete();
    }
}
