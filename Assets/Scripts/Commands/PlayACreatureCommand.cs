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

    public PlayACreatureCommand(CardLogic cl, Player p, int tablePos, int creatureID, PlayerArea selectedPArea)
    {
        this.p = p;
        this.cl = cl;
        this.tablePos = tablePos;
        this.creatureID = creatureID;
        this.selectedPArea = selectedPArea;
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

        GameObject existingCreature = IDHolder.GetGameObjectWithID(creatureID);
        if (existingCreature != null)
        {
            // Use the current visual position so any reorder the player did is preserved
            bool isMelee = cl.ca.melee;
            List<GameObject> visualList = isMelee
                ? selectedPArea.tableVisual.MeleeCreaturesOnTable
                : selectedPArea.tableVisual.RangedCreaturesOnTable;
            int currentVisualPos = visualList.IndexOf(existingCreature);
            int resolvedPos = currentVisualPos >= 0 ? currentVisualPos : tablePos;

            selectedPArea.tableVisual.PendingCreaturesOnTable.Remove(existingCreature);
            selectedPArea.tableVisual.MeleeCreaturesOnTable.Remove(existingCreature);
            selectedPArea.tableVisual.RangedCreaturesOnTable.Remove(existingCreature);
            selectedPArea.tableVisual.MoveCreatureToIndex(existingCreature, creatureID, resolvedPos, selectedPArea.baseID);
            if (existingCreature.TryGetComponent(out OneCreatureManager ocm)) ocm.SetPending(false);
        }
        else
        {
            selectedPArea.tableVisual.AddCreatureAtIndex(cl.ca, creatureID, tablePos, selectedPArea.baseID);
        }
        BuildSpotVisual.RefreshAll();

    }
}
