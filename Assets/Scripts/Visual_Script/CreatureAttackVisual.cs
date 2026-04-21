using UnityEngine;
using System.Collections;
using DG.Tweening;

public class CreatureAttackVisual : MonoBehaviour 
{
    private OneCreatureManager manager;
    private WhereIsTheCardOrCreature w;

    void Awake()
    {
        manager = GetComponent<OneCreatureManager>();    
        w = GetComponent<WhereIsTheCardOrCreature>();
    }

    private string GetTargetType(int targetUniqueID)
    {
        if (
            targetUniqueID == GlobalSettings.Instance.LowPlayer.PlayerID 
            || targetUniqueID == GlobalSettings.Instance.TopPlayer.PlayerID
        )
        {
            return "player";
        }
        else if (BuildingLogic.BuildingsCreatedThisGame.ContainsKey(targetUniqueID) &&
                 BuildingLogic.BuildingsCreatedThisGame[targetUniqueID] != null)
        {
            return "building";
        }
        else if (CreatureLogic.CreaturesCreatedThisGame.ContainsKey(targetUniqueID) &&
                 CreatureLogic.CreaturesCreatedThisGame[targetUniqueID] != null)
        {
            return "creature";
        }
        return "unknown";
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
        string targetType = GetTargetType(targetUniqueID);

        // bring this creature to front sorting-wise.
        w.BringToFront();
        VisualStates tempState = w.VisualState;
        w.VisualState = VisualStates.Transition;

        // SetLink tue automatiquement le tween si ce GameObject est détruit pendant l'animation
        transform.DOMove(target.transform.position, 0.5f)
            .SetLoops(2, LoopType.Yoyo)
            .SetEase(Ease.InCubic)
            .SetLink(gameObject)
            .OnComplete(() =>
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
                        case "player":
                            target.GetComponent<BaseVisual>().HealthText.text = targetHealthAfter.ToString();
                            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
                            break;
                        case "building":
                            target.GetComponent<OneBuildingManager>().HealthText.text = targetHealthAfter.ToString();
                            break;
                        case "creature":
                            target.GetComponent<OneCreatureManager>().HealthText.text = targetHealthAfter.ToString();
                            break;
                        case "unknown":
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
            });
    }
        
}
