using UnityEngine;
using DG.Tweening;

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
        Debug.Log($"[BuildingAttackCmd] attaquant={attackerID} → cible={targetID} dégâts cible={damageTakenByTarget} dégâts attaquant={damageTakenByAttacker}");
        GameObject attackerGO = IDHolder.GetGameObjectWithID(attackerID);
        GameObject targetGO = IDHolder.GetGameObjectWithID(targetID);

        if (attackerGO == null || targetGO == null)
        {
            Debug.LogWarning($"[BuildingAttackCmd] attaquant ou cible introuvable (attaquant={attackerGO != null}, cible={targetGO != null}) — CommandExecutionComplete forcé");
            ApplyEffects(attackerGO, targetGO);
            CommandExecutionComplete();
            return;
        }

        attackerGO.transform.DOMove(targetGO.transform.position, 0.3f)
            .SetLoops(2, LoopType.Yoyo)
            .SetEase(Ease.InBack)
            .SetLink(attackerGO)
            .OnComplete(() =>
            {
                try
                {
                    ApplyEffects(attackerGO, targetGO);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[BuildingAttackCmd] EXCEPTION pendant ApplyEffects (attaquant={attackerID}, cible={targetID}) — file débloquée de force: {e}");
                }
                CommandExecutionComplete();
            });
    }

    void ApplyEffects(GameObject attackerGO, GameObject targetGO)
    {
        if (attackerGO != null)
        {
            if (damageTakenByAttacker > 0)
                VisualFeedbackEffect.CreateDamageEffect(attackerGO.transform.position, damageTakenByAttacker);
            if (attackerGO.GetComponent<OneBuildingManager>() is OneBuildingManager am)
                am.HealthText.text = attackerHealthAfter.ToString();
        }

        if (targetGO != null)
        {
            if (damageTakenByTarget > 0)
                VisualFeedbackEffect.CreateDamageEffect(targetGO.transform.position, damageTakenByTarget);

            if (BuildingLogic.BuildingsCreatedThisGame.ContainsKey(targetID))
            {
                if (targetGO.GetComponent<OneBuildingManager>() is OneBuildingManager bm)
                    bm.HealthText.text = targetHealthAfter.ToString();
            }
            else if (CreatureLogic.CreaturesCreatedThisGame.ContainsKey(targetID))
            {
                if (targetGO.GetComponent<OneCreatureManager>() is OneCreatureManager cm)
                    cm.HealthText.text = targetHealthAfter.ToString();
            }
            else if (BaseLogic.BasesCreatedThisGame.ContainsKey(targetID))
            {
                if (targetGO.GetComponent<OneBaseManager>() is OneBaseManager om)
                    om.HealthText.text = targetHealthAfter.ToString();
            }
            else
            {
                if (targetGO.GetComponent<MainBaseVisual>() is MainBaseVisual mbv)
                    mbv.HealthText.text = targetHealthAfter.ToString();
                GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
            }
        }
    }

}
