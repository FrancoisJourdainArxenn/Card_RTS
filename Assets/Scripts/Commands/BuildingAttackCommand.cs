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

        void PlayAttack()
        {
            const float moveDuration = 0.3f;

            // Shake fires a little before the building actually reaches the target (impact = moveDuration),
            // not after ApplyEffects (which only runs once the whole there-and-back move finishes) — otherwise
            // it lands visibly late. Only the attacker's own hit shakes the camera, never the defender's counter-damage.
            if (damageTakenByTarget > 0)
            {
                float leadTime = GlobalSettings.Instance != null ? GlobalSettings.Instance.CameraShakeAnticipation : 0.05f;
                float shakeDelay = Mathf.Max(0f, moveDuration - leadTime);
                DOVirtual.DelayedCall(shakeDelay, () => CameraController.Instance?.ShakeForAttackerID(attackerID))
                    .SetLink(attackerGO);
            }

            attackerGO.transform.DOMove(targetGO.transform.position, moveDuration)
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

        if (CameraController.Instance != null)
            CameraController.Instance.FocusBattleCamOn(attackerGO.transform.position, PlayAttack);
        else
            PlayAttack();
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
