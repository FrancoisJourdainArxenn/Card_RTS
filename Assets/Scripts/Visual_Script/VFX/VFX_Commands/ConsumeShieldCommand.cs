using UnityEngine;

// Rejoue, à sa place dans la file de commandes visuelles, l'absorption de bouclier calculée par
// CreatureLogic.ConsumeShieldQueued pendant ZoneCombatResolver.EnqueueBattleCommands. Toujours mise en
// file après le ApplyShieldCommand qui a affiché le bouclier consommé ici (même ordre que les steps de
// combat qui les ont générés), donc l'instance VFX du bouclier existe déjà quand cette commande s'exécute.
public class ConsumeShieldCommand : Command
{
    private readonly int targetID;
    // Montant réellement absorbé par CE coup (delta) — pas un total restant. VfxManager.ConsumeShieldVfx
    // le soustrait à ce qui est déjà affiché, cohérent avec les gains rejoués par ApplyShieldCommand
    // dans le même ordre visuel, plutôt que d'écraser avec CreatureLogic.ShieldValue (qui peut déjà
    // refléter des gains bien plus tardifs de la même planification — voir VfxManager._shieldAmount).
    private readonly int absorbed;

    public ConsumeShieldCommand(int targetID, int absorbed)
    {
        this.targetID = targetID;
        this.absorbed = absorbed;
    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);

        if (target != null && target.TryGetComponent(out VfxManager vfx))
        {
            vfx.ConsumeShieldVfx(absorbed);
        }
        else
        {
            Debug.LogWarning($"[Shield/VFX] ConsumeShieldCommand — objet visuel introuvable pour targetID={targetID} (absorbed={absorbed}) — affichage NON mis à jour, désync probable");
        }

        CommandExecutionComplete();
    }
}
