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

    public void AttackTarget(int targetUniqueID, int damageTakenByTarget, int damageTakenByAttacker, int attackerHealthAfter, int targetHealthAfter)
    {
        // L'attaquant peut avoir été détruit entre l'enregistrement et l'exécution de la commande
        if (this == null)
        {
            Command.CommandExecutionComplete();
            return;
        }

        manager.CanAttackNow = false;
        GameObject target = IDHolder.GetGameObjectWithID(targetUniqueID);
        if (target == null)
        {
            Debug.Log("No Target");
            manager.HealthText.text = attackerHealthAfter.ToString();
            Command.CommandExecutionComplete();
            return;
        }
        AttackTargetType targetType = GetTargetType(targetUniqueID);

        // bring this creature to front sorting-wise.
        w.BringToFront();
        VisualStates tempState = w.VisualState;
        w.VisualState = VisualStates.Transition;

        // SetLink tue automatiquement le tween si ce GameObject est détruit pendant l'animation
        transform.DOMove(target.transform.position, 0.5f)
            .SetLoops(2, LoopType.Yoyo)
            .SetEase(Ease.InCubic)
            .SetLink(gameObject)
            .OnComplete((TweenCallback)(() =>
            {
                // L'attaquant ou la cible peut avoir été détruit pendant l'animation
                if (this == null)
                {
                    Command.CommandExecutionComplete();
                    return;
                }

                if (damageTakenByTarget > 0 && target != null)
                    DamageEffect.CreateDamageEffect(target.transform.position, damageTakenByTarget);
                if (damageTakenByAttacker > 0)
                    DamageEffect.CreateDamageEffect(transform.position, damageTakenByAttacker);

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
                Sequence s = DOTween.Sequence();
                s.AppendInterval(1f);
                s.SetLink(gameObject);
                s.OnComplete(Command.CommandExecutionComplete);
            }));
    }
        
}
