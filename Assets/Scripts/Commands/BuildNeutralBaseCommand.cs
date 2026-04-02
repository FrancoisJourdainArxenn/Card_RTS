using UnityEngine;

public class BuildNeutralBaseCommand : Command
{
    private Player player;
    private BaseAsset neutralBaseAsset;
    private NeutralBaseVisual spawner;
    private Transform basePosition;
    private int buildingUniqueID;
    
    public BuildNeutralBaseCommand(int buildingUniqueID, Player player, NeutralBaseVisual spawner, BaseAsset neutralBaseAsset)
    {
        this.player = player;
        this.buildingUniqueID = buildingUniqueID;
        this.spawner = spawner;
        this.neutralBaseAsset = neutralBaseAsset;
    }

    public override void StartCommandExecution()
    {
        if (player == null || spawner == null)
        {
            Debug.LogWarning("BuildNeutralBaseCommand: player ou spawner est null.");
            CommandExecutionComplete();
            return;
        }

        player.MainRessourceAvailable -= neutralBaseAsset.mainRessourceBuildingCost;
        player.SecondRessourceAvailable -= neutralBaseAsset.secondRessourceBuildingCost;
        Debug.Log("Player :" + player.name + " has build a base.");
        new UpdateRessourcesCommand(player, player.mainRessourceTotal, player.MainRessourceAvailable, player.secondRessourceTotal, player.SecondRessourceAvailable).AddToQueue();
        spawner.InstantiateBaseCard(player, buildingUniqueID);
        CommandExecutionComplete();

    }
}
