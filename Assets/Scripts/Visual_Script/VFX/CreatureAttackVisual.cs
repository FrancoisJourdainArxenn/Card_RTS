using UnityEngine;
using System.Collections;
using DG.Tweening;

enum AttackTargetType
{
    Player,
    Base,
    Creature,
    Building,
    Unknown
}

public class CreatureAttackVisual : MonoBehaviour 
{
    private OneCreatureManager manager;
    private WhereIsTheCardOrCreature w;
    float moveDuration = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackMoveDuration : 0.4f;
    float postDelay    = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackPostDelay : 0.8f;

    void Awake()
    {
        manager = GetComponent<OneCreatureManager>();
        w = GetComponent<WhereIsTheCardOrCreature>();
    }

    private AttackTargetType GetTargetType(int targetUniqueID)
    {
        if (
            targetUniqueID == GlobalSettings.Instance.LowPlayer.PlayerID 
            || targetUniqueID == GlobalSettings.Instance.TopPlayer.PlayerID
        )
        {
            return AttackTargetType.Player;
        }
        else if (BaseLogic.BasesCreatedThisGame.ContainsKey(targetUniqueID) &&
                 BaseLogic.BasesCreatedThisGame[targetUniqueID] != null)
        {
            return AttackTargetType.Base;
        }
        else if (CreatureLogic.CreaturesCreatedThisGame.ContainsKey(targetUniqueID) &&
                 CreatureLogic.CreaturesCreatedThisGame[targetUniqueID] != null)
        {
            return AttackTargetType.Creature;
        }
        else if (BuildingLogic.BuildingsCreatedThisGame.ContainsKey(targetUniqueID) &&
                 BuildingLogic.BuildingsCreatedThisGame[targetUniqueID] != null)
        {
            return AttackTargetType.Building;
        }
        return AttackTargetType.Unknown;
    }

    public void AttackTarget(int targetUniqueID, int damageTakenByTarget, int damageTakenByAttacker, int attackerHealthAfter, int targetHealthAfter, float speedMultiplier = 1f)
    {
        Debug.Log($"[AttackVisual] {gameObject.name} → cible ID:{targetUniqueID} | dégâts cible:{damageTakenByTarget} dégâts attaquant:{damageTakenByAttacker}");
        // L'attaquant peut avoir été détruit entre l'enregistrement et l'exécution de la commande
        if (this == null)
        {
            Debug.LogWarning("[AttackVisual] attaquant détruit avant exécution — CommandExecutionComplete forcé");
            Command.CommandExecutionComplete();
            return;
        }

        GameObject target = IDHolder.GetGameObjectWithID(targetUniqueID);
        if (target == null)
        {
            Debug.LogWarning($"[AttackVisual] Cible ID:{targetUniqueID} introuvable (IDHolder) — CommandExecutionComplete forcé");
            manager.HealthText.text = attackerHealthAfter.ToString();
            Command.CommandExecutionComplete();
            return;
        }
        AttackTargetType targetType = GetTargetType(targetUniqueID);
        float moveDur  = moveDuration / speedMultiplier;
        float postDel  = postDelay    / speedMultiplier;
        float windupDur = (GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackWindupDuration : 0.15f) / speedMultiplier;
        float windupBack   = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackWindupBack   : 0.3f;
        float windupHeight = GlobalSettings.Instance != null ? GlobalSettings.Instance.AttackWindupHeight : 0.35f;

        // bring this creature to front sorting-wise.
        w.BringToFront();
        VisualStates tempState = w.VisualState;
        w.VisualState = VisualStates.Transition;
        Vector3 originalPosition = transform.position;

        Vector3 flatDir = target.transform.position - originalPosition;
        flatDir.y = 0f;
        flatDir = flatDir.sqrMagnitude > 0.0001f ? flatDir.normalized : transform.forward;
        Vector3 windupPosition = originalPosition - flatDir * windupBack + Vector3.up * windupHeight;

        bool moveDone = false;
        Sequence attackSeq = DOTween.Sequence();
        attackSeq.SetLink(gameObject);
        attackSeq.Append(transform.DOMove(windupPosition, windupDur).SetEase(Ease.OutSine));
        attackSeq.Append(transform.DOMove(target.transform.position, moveDur).SetEase(Ease.InExpo));
        attackSeq.Append(transform.DOMove(originalPosition, moveDur).SetEase(Ease.OutSine));

        // Shake fires a little before the creature actually reaches the target (impact = windupDur + moveDur),
        // not on the sequence's OnComplete (which only fires after the return move too) — otherwise it lands
        // visibly late. Only the attacker's own hit shakes the camera, never the defender's counter-damage.
        if (damageTakenByTarget > 0)
        {
            IDHolder selfID = GetComponent<IDHolder>();
            if (selfID != null)
            {
                int attackerUniqueID = selfID.UniqueID;
                float leadTime = GlobalSettings.Instance != null ? GlobalSettings.Instance.CameraShakeAnticipation : 0.05f;
                float shakeTime = Mathf.Max(0f, windupDur + moveDur - leadTime);
                attackSeq.InsertCallback(shakeTime, () => CameraController.Instance?.ShakeForAttackerID(attackerUniqueID));
            }
        }

        attackSeq
            .OnComplete((TweenCallback)(() =>
            {
                moveDone = true;
                try
                {
                    if (this == null)
                    {
                        Command.CommandExecutionComplete();
                        return;
                    }

                    if (damageTakenByTarget > 0 && target != null)
                        VisualFeedbackEffect.CreateDamageEffect(target.transform.position, damageTakenByTarget);
                    if (damageTakenByAttacker > 0)
                        VisualFeedbackEffect.CreateDamageEffect(transform.position, damageTakenByAttacker);

                    if (target != null)
                    {
                        switch (targetType)
                        {
                            case AttackTargetType.Player:
                                target.GetComponent<MainBaseVisual>().HealthText.text = targetHealthAfter.ToString();
                                GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
                                break;
                            case AttackTargetType.Base:
                                target.GetComponent<OneBaseManager>().HealthText.text = targetHealthAfter.ToString();
                                break;
                            case AttackTargetType.Creature:
                                target.GetComponent<OneCreatureManager>().HealthText.text = targetHealthAfter.ToString();
                                break;
                            case AttackTargetType.Building:
                                target.GetComponent<OneBuildingManager>().HealthText.text = targetHealthAfter.ToString();
                                break;
                            case AttackTargetType.Unknown:
                                Debug.Log("Unknown target type: " + targetUniqueID);
                                break;
                        }
                    }

                    w.SetTableSortingOrder();
                    w.VisualState = tempState;

                    manager.HealthText.text = attackerHealthAfter.ToString();
                    bool seqDone = false;
                    Sequence s = DOTween.Sequence();
                    s.AppendInterval(postDel);
                    s.SetLink(gameObject);
                    s.OnComplete(() => { seqDone = true; Command.CommandExecutionComplete(); });
                    s.OnKill(() => { if (!seqDone) Command.CommandExecutionComplete(); });
                }
                catch (System.Exception e)
                {
                    // Si cette exception se produit, la file de commandes (Command.CommandQueue) se bloquerait
                    // silencieusement pour toujours sans ce filet — d'où le log explicite ici.
                    Debug.LogError($"[AttackVisual] EXCEPTION pendant l'animation de {gameObject.name} → cible ID:{targetUniqueID} (type={targetType}) — file débloquée de force: {e}");
                    Command.CommandExecutionComplete();
                }
            }))
            .OnKill(() => { if (!moveDone) Command.CommandExecutionComplete(); });
    }
        
}
