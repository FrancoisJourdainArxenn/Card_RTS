using UnityEngine;
using System.Collections;

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
            p.MainPArea.handVisual.RemoveCard(card);
            GameObject.Destroy(card);
        }
        HoverPreview.PreviewsAllowed = true;

        GameObject existingCreature = IDHolder.GetGameObjectWithID(creatureID);
        if (existingCreature != null)
        {
            selectedPArea.tableVisual.PendingCreaturesOnTable.Remove(existingCreature);
            selectedPArea.tableVisual.MoveCreatureToIndex(existingCreature, creatureID, tablePos, selectedPArea.baseID);
            existingCreature.GetComponent<OneCreatureManager>().SetGray(false);
        }
        else
        {
            selectedPArea.tableVisual.AddCreatureAtIndex(cl.ca, creatureID, tablePos, selectedPArea.baseID);
        }
    }
}
