using UnityEngine;
using System.Collections;

public class CreatureAttackCommand : Command 
{
    // position of creature on enemy`s table that will be attacked
    // if enemyindex == -1 , attack an enemy character 
    private int TargetUniqueID;
    private int AttackerUniqueID;
    private int AttackerHealthAfter;
    private int TargetHealthAfter;
    private int DamageTakenByAttacker;
    private int DamageTakenByTarget;
    private float SpeedMultiplier;


    public CreatureAttackCommand(int targetID, int attackerID, int damageTakenByAttacker, int damageTakenByTarget, int attackerHealthAfter, int targetHealthAfter, float speedMultiplier = 1f)
    {
        this.TargetUniqueID = targetID;
        this.AttackerUniqueID = attackerID;
        this.AttackerHealthAfter = attackerHealthAfter;
        this.TargetHealthAfter = targetHealthAfter;
        this.DamageTakenByTarget = damageTakenByTarget;
        this.DamageTakenByAttacker = damageTakenByAttacker;
        this.SpeedMultiplier = speedMultiplier;
    }

    public override void StartCommandExecution()
    {
        GameObject attacker = IDHolder.GetGameObjectWithID(AttackerUniqueID);
        Debug.Log($"[AttackCmd] {attacker?.name ?? $"ID:{AttackerUniqueID}(null)"} attaque ID:{TargetUniqueID}");
        if (attacker == null) { CommandExecutionComplete(); return; }
        CreatureAttackVisual visual = attacker.GetComponent<CreatureAttackVisual>();
        if (visual == null) { CommandExecutionComplete(); return; }

        void PlayAttack() =>
            visual.AttackTarget(TargetUniqueID, DamageTakenByTarget, DamageTakenByAttacker, AttackerHealthAfter, TargetHealthAfter, SpeedMultiplier);

        if (CameraController.Instance != null)
            CameraController.Instance.FocusBattleCamOn(attacker.transform.position, PlayAttack);
        else
            PlayAttack();
    }
}
