using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    // Cibles secondaires touchées par les modificateurs d'attaque (Cone/Piercing/...) de l'attaquant,
    // dégâts déjà appliqués en logique — jouées dans la même séquence visuelle que la cible principale.
    private List<AttackHitResult> SecondaryHits;


    public CreatureAttackCommand(int targetID, int attackerID, int damageTakenByAttacker, int damageTakenByTarget, int attackerHealthAfter, int targetHealthAfter, float speedMultiplier = 1f, List<AttackHitResult> secondaryHits = null)
    {
        this.TargetUniqueID = targetID;
        this.AttackerUniqueID = attackerID;
        this.AttackerHealthAfter = attackerHealthAfter;
        this.TargetHealthAfter = targetHealthAfter;
        this.DamageTakenByTarget = damageTakenByTarget;
        this.DamageTakenByAttacker = damageTakenByAttacker;
        this.SpeedMultiplier = speedMultiplier;
        this.SecondaryHits = secondaryHits ?? new List<AttackHitResult>();
    }

    public override void StartCommandExecution()
    {
        GameObject attacker = IDHolder.GetGameObjectWithID(AttackerUniqueID);
        Debug.Log($"[AttackCmd] {attacker?.name ?? $"ID:{AttackerUniqueID}(null)"} attaque ID:{TargetUniqueID}");
        if (attacker == null) { CommandExecutionComplete(); return; }
        CreatureAttackVisual visual = attacker.GetComponent<CreatureAttackVisual>();
        if (visual == null) { CommandExecutionComplete(); return; }

        void PlayAttack() =>
            visual.AttackTarget(TargetUniqueID, DamageTakenByTarget, DamageTakenByAttacker, AttackerHealthAfter, TargetHealthAfter, SpeedMultiplier, SecondaryHits);

        if (CameraController.Instance != null)
            CameraController.Instance.FocusBattleCamOn(attacker.transform.position, PlayAttack);
        else
            PlayAttack();
    }

    // Point d'entrée du chemin auto-battle (ZoneCombatResolver.EnqueueBattleCommands) : découpe
    // l'attaque en 2 Commands séparées (wind-up, puis résolution) avec un flush entre les deux, pour
    // que les commandes différées d'un effet OnAttack (buff/token, mises en file via
    // Command.RunDeferred(attackerID, ...) pendant CreatureLogic.ResolvePredictedOnAttack) s'intercalent
    // exactement entre le recul et la charge/le tir — même principe que OnDeath, qui intercale ses
    // commandes différées juste avant la CreatureDieCommand (voir CreatureLogic.MarkPendingDeath).
    //
    // Le bucket différé de attackerID est déjà peuplé à ce stade sur TOUTES les machines (le serveur l'a
    // résolu pour de vrai pendant sa planification, les clients l'ont rejoué via
    // CreatureLogic.ReplayPredictedTriggerEffect avant que BroadcastBattleStepsClientRpc ne déclenche cet
    // appel) — ce flush ne fait donc que rejouer un état déjà identique des deux côtés, il ne prend aucune
    // décision lui-même.
    //
    // Flush sous CreatureLogic.OnAttackDeferKey(attackerID), PAS attackerID tel quel : un attaquant peut
    // être prédictivement tué par la contre-attaque de sa propre cible PENDANT la résolution de CETTE
    // même attaque (ZoneCombatResolver.AddPendingCreatureDamage → ResolvePredictedBattleDeath, sur
    // l'attaquant lui-même) — son éventuel OnDeath est alors déjà en attente sous la clé attackerID brute.
    // Flusher cette clé brute ici viderait son buff/token OnDeath en plein wind-up au lieu de juste avant
    // sa CreatureDieCommand (bug observé : token apparu alors que la créature n'était ni morte ni n'avait
    // fini d'attaquer). La clé dédiée élimine cette collision par construction.
    public static void EnqueueAttack(int targetID, int attackerID, int damageTakenByAttacker, int damageTakenByTarget, int attackerHealthAfter, int targetHealthAfter, float speedMultiplier = 1f, List<AttackHitResult> secondaryHits = null)
    {
        new CreatureAttackWindupCommand(attackerID, targetID).AddToQueue();
        int onAttackKey = CreatureLogic.OnAttackDeferKey(attackerID);
        if (Command.HasDeferredCommands(onAttackKey))
            Debug.Log($"[OnAttack] attackerID={attackerID} — flush du bucket OnAttack entre wind-up et résolution");
        Command.FlushDeferredCommands(onAttackKey);
        new CreatureAttackResolveCommand(targetID, attackerID, damageTakenByAttacker, damageTakenByTarget, attackerHealthAfter, targetHealthAfter, speedMultiplier, secondaryHits).AddToQueue();
    }
}

// Phase 1 de l'attaque auto-battle : le recul (wind-up) seul. Voir CreatureAttackCommand.EnqueueAttack.
public class CreatureAttackWindupCommand : Command
{
    private readonly int AttackerUniqueID;
    private readonly int TargetUniqueID;

    public CreatureAttackWindupCommand(int attackerID, int targetID)
    {
        AttackerUniqueID = attackerID;
        TargetUniqueID = targetID;
    }

    public override void StartCommandExecution()
    {
        GameObject attacker = IDHolder.GetGameObjectWithID(AttackerUniqueID);
        if (attacker == null) { CommandExecutionComplete(); return; }
        CreatureAttackVisual visual = attacker.GetComponent<CreatureAttackVisual>();
        if (visual == null) { CommandExecutionComplete(); return; }

        void PlayWindup() =>
            visual.PlayWindup(TargetUniqueID, () => CommandExecutionComplete());

        if (CameraController.Instance != null)
            CameraController.Instance.FocusBattleCamOn(attacker.transform.position, PlayWindup);
        else
            PlayWindup();
    }
}

// Phase 2 de l'attaque auto-battle : charge/tir, impact, retour. Voir CreatureAttackCommand.EnqueueAttack.
public class CreatureAttackResolveCommand : Command
{
    private readonly int TargetUniqueID;
    private readonly int AttackerUniqueID;
    private readonly int AttackerHealthAfter;
    private readonly int TargetHealthAfter;
    private readonly int DamageTakenByAttacker;
    private readonly int DamageTakenByTarget;
    private readonly float SpeedMultiplier;
    private readonly List<AttackHitResult> SecondaryHits;

    public CreatureAttackResolveCommand(int targetID, int attackerID, int damageTakenByAttacker, int damageTakenByTarget, int attackerHealthAfter, int targetHealthAfter, float speedMultiplier = 1f, List<AttackHitResult> secondaryHits = null)
    {
        TargetUniqueID = targetID;
        AttackerUniqueID = attackerID;
        AttackerHealthAfter = attackerHealthAfter;
        TargetHealthAfter = targetHealthAfter;
        DamageTakenByTarget = damageTakenByTarget;
        DamageTakenByAttacker = damageTakenByAttacker;
        SpeedMultiplier = speedMultiplier;
        SecondaryHits = secondaryHits ?? new List<AttackHitResult>();
    }

    public override void StartCommandExecution()
    {
        GameObject attacker = IDHolder.GetGameObjectWithID(AttackerUniqueID);
        if (attacker == null) { CommandExecutionComplete(); return; }
        CreatureAttackVisual visual = attacker.GetComponent<CreatureAttackVisual>();
        if (visual == null) { CommandExecutionComplete(); return; }

        visual.PlayResolve(TargetUniqueID, DamageTakenByTarget, DamageTakenByAttacker, AttackerHealthAfter, TargetHealthAfter, SpeedMultiplier, SecondaryHits);
    }
}
