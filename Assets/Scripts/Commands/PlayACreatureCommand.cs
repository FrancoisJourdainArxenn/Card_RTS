using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayACreatureCommand : Command
{
    private CardLogic cl;
    private int tablePos;
    private Player p;
    private int creatureID;
    private PlayerArea selectedPArea;
    // Stats de spawn capturées AVANT toute mutation de combat (voir TokenGenerationSO.SpawnToZone) —
    // utilisées à la place de CreatureLogic.Attack/Health "live", déjà potentiellement mutés par
    // EnqueueBattleCommands au moment où cette commande s'exécute réellement.
    private int? spawnAttack;
    private int? spawnHealth;

    public PlayACreatureCommand(CardLogic cl, Player p, int tablePos, int creatureID, PlayerArea selectedPArea, int? spawnAttack = null, int? spawnHealth = null)
    {
        this.p = p;
        this.cl = cl;
        this.tablePos = tablePos;
        this.creatureID = creatureID;
        this.selectedPArea = selectedPArea;
        this.spawnAttack = spawnAttack;
        this.spawnHealth = spawnHealth;
    }

    public override void StartCommandExecution()
    {
        GameObject card = IDHolder.GetGameObjectWithID(cl.UniqueCardID);
        if (card != null)
        {
            p.handVisual.RemoveCard(card);
            GameObject.Destroy(card);
        }
        HoverPreview.PreviewsAllowed = true;

        // Une créature tuée par un effet résolu DANS la même phase où elle a été jouée (ex: Assimilate
        // ciblant un allié encore "pending") voit son GameObject détruit avant que ce flush (déclenché
        // en fin de phase, voir GameNetworkManager.FlushBuffer) ne s'exécute pour elle. Sans ce garde,
        // la branche "else" ci-dessous la recrée de toutes pièces via AddCreatureAtIndex — un
        // GameObject flambant neuf, entièrement fonctionnel, pour une créature déjà morte (contrairement
        // au fantôme partiel du bug ResyncCreatureOrderForArea, celui-ci passe par le vrai pipeline de
        // création et reste donc pleinement interactif).
        bool found = CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureID, out CreatureLogic creatureLogic);
        if (!found || creatureLogic.IsPendingDeath || creatureLogic.Health <= 0)
        {
            Debug.LogWarning($"[PlayACreatureCommand] SKIP reveal — créature {creatureID} : trouvée={found}, IsPendingDeath={(found ? creatureLogic.IsPendingDeath : (bool?)null)}, Health={(found ? creatureLogic.Health : (int?)null)}.");
            Command.CommandExecutionComplete();
            return;
        }
        Debug.Log($"[PlayACreatureCommand] REVEAL — créature {creatureID} ({creatureLogic.DisplayName}) Health={creatureLogic.Health}, GO existant={(IDHolder.GetGameObjectWithID(creatureID) != null)}.");

        GameObject existingCreature = IDHolder.GetGameObjectWithID(creatureID);
        if (existingCreature != null)
        {
            // Use the current visual position so any reorder the player did is preserved
            bool isMelee = cl.ca.melee;
            List<GameObject> visualList = isMelee
                ? selectedPArea.tableVisual.MeleeCreaturesOnTable
                : selectedPArea.tableVisual.RangedCreaturesOnTable;
            int currentVisualPos = visualList.IndexOf(existingCreature);
            int resolvedPos = currentVisualPos >= 0
                ? currentVisualPos
                : selectedPArea.tableVisual.FromNetworkTablePos(isMelee, tablePos);

            selectedPArea.tableVisual.PendingCreaturesOnTable.Remove(existingCreature);
            selectedPArea.tableVisual.MeleeCreaturesOnTable.Remove(existingCreature);
            selectedPArea.tableVisual.RangedCreaturesOnTable.Remove(existingCreature);
            selectedPArea.tableVisual.MoveCreatureToIndex(existingCreature, creatureID, resolvedPos, selectedPArea.baseID);
            if (existingCreature.TryGetComponent(out OneCreatureManager ocm)) ocm.SetPending(false);
        }
        else
        {
            // tablePos est logique (ghosts exclus, voir TableVisual.ToNetworkTablePos) : on le
            // reconvertit en index de liste réel pour CE client avant d'insérer.
            int rawTablePos = selectedPArea.tableVisual.FromNetworkTablePos(cl.ca.melee, tablePos);
            selectedPArea.tableVisual.AddCreatureAtIndex(cl.ca, creatureID, rawTablePos, selectedPArea.baseID, overrideAttack: spawnAttack, overrideHealth: spawnHealth);
        }
        BuildSpotVisual.RefreshAll();

    }
}
