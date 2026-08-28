using UnityEngine;
using System.Collections;

public class CreatureDieCommand : Command 
{
    private Player p;
    private int DeadCreatureID;

    public CreatureDieCommand(int CreatureID, Player p)
    {
        this.p = p;
        this.DeadCreatureID = CreatureID;
    }

    public override void StartCommandExecution()
    {
        // Marqué dès que cette commande a son tour dans la file, GO trouvé ou non — voir
        // CreatureLogic.DieCommandExecuted : c'est ce flag, pas IsPendingDeath, que PlayACreatureCommand
        // consulte pour décider s'il est encore légitime de révéler cette créature.
        if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(DeadCreatureID, out CreatureLogic diedCreature))
            diedCreature.MarkDieCommandExecuted();

        GameObject creatureToRemove = IDHolder.GetGameObjectWithID(DeadCreatureID);
        if (creatureToRemove == null)
        {
            Command.CommandExecutionComplete();
            return;
        }

        // Une créature qui meurt alors qu'un déplacement est en attente (ex: Assimilate ciblant une
        // unité juste glissée vers une autre zone) laisse sinon son ghost orphelin dans la zone cible :
        // seuls les chemins de résolution normale d'un déplacement (FlushSoloMoveBuffer,
        // MoveCreatureClientRpc) nettoient ce ghost, jamais le chemin de mort.
        creatureToRemove.GetComponent<OneCreatureManager>()?.DestroyPendingMoveGhost();

        VfxManager vfx = creatureToRemove.GetComponent<VfxManager>();

        // Le VFX de trigger OnDeath doit se jouer ici, tant que le GameObject existe encore : passé
        // ce point, il est détruit (voir plus bas) avant que la command du trigger OnDeath
        // (enfilée par EffectRegistry.Execute) n'ait la moindre chance de s'exécuter — voir
        // CreatureLogic.MarkPendingDeath, qui enfile toujours CreatureDieCommand avant de libérer
        // les commandes différées de l'effet OnDeath.
        if (vfx != null && TriggerAnimationLibrary.Instance != null)
        {
            CardAsset cardAsset = creatureToRemove.GetComponent<OneCreatureManager>()?.cardAsset;
            bool hasOnDeathEffect = cardAsset != null && cardAsset.Effects != null
                && cardAsset.Effects.Exists(e => e.Trigger == TriggerType.OnDeath);

            if (hasOnDeathEffect)
            {
                GameObject onDeathVfxPrefab = TriggerAnimationLibrary.Instance.GetVfxPrefab(TriggerType.OnDeath);
                if (onDeathVfxPrefab != null)
                    vfx.PlayTriggerVfx(onDeathVfxPrefab);
            }
        }

        float deathVfxDuration = vfx != null ? vfx.PlayDeath() : 0f;

        if (p.PAreas != null)
        {
            foreach (PlayerArea area in p.PAreas)
            {
                if (area == null || area.tableVisual == null)
                    continue;

                if (area.tableVisual.MeleeCreaturesOnTable.Contains(creatureToRemove) ||
                    area.tableVisual.RangedCreaturesOnTable.Contains(creatureToRemove))
                {
                    // La créature disparaît immédiatement ; seul le re-order de la rangée attend la
                    // durée du VFX de mort (voir TableVisual.RemoveCreatureWithID), qui appelle
                    // CommandExecutionComplete lui-même une fois le repositionnement terminé.
                    area.tableVisual.RemoveCreatureWithID(DeadCreatureID, deathVfxDuration);
                    return;
                }
            }
        }

        //Debug.LogWarning("CreatureDieCommand: créature " + DeadCreatureID + " introuvable sur les tables de ce joueur.");
        Object.Destroy(creatureToRemove);
        BuildSpotVisual.RefreshAll();
        Command.CommandExecutionComplete();
    }
}
