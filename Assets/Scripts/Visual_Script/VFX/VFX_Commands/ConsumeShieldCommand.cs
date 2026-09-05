using UnityEngine;

// Rejoue, à sa place dans la file de commandes visuelles, l'absorption de bouclier calculée par
// CreatureLogic.ConsumeShieldQueued pendant ZoneCombatResolver.EnqueueBattleCommands. Toujours mise en
// file après le ApplyShieldCommand qui a affiché le bouclier consommé ici (même ordre que les steps de
// combat qui les ont générés), donc l'instance VFX du bouclier existe déjà quand cette commande s'exécute.
public class ConsumeShieldCommand : Command
{
    private readonly int targetID;
    private readonly int remainingShield;

    public ConsumeShieldCommand(int targetID, int remainingShield)
    {
        this.targetID = targetID;
        this.remainingShield = remainingShield;
    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);

        if (target != null && target.TryGetComponent(out VfxManager vfx))
        {
            if (remainingShield > 0)
                vfx.UpdateShieldVfx(remainingShield);
            else
                vfx.HideShieldVfx();
        }
        else
        {
            Debug.LogWarning($"[Shield/VFX] ConsumeShieldCommand — objet visuel introuvable pour targetID={targetID} (remainingShield={remainingShield}) — affichage NON mis à jour, désync probable");
        }

        CommandExecutionComplete();
    }
}
