using UnityEngine;

public class BuildNeutralBaseCommand : Command
{
    private Player player;
    private BaseAsset neutralBaseAsset;
    private Transform basePosition;
    public BuildNeutralBaseCommand(Player player, BaseAsset neutralBaseAsset)
    {
        this.player = player;
        this.neutralBaseAsset = neutralBaseAsset;

    }

    public override void StartCommandExecution()
    {
        if (player == null || neutralBaseAsset == null)
        {
            Debug.LogWarning("BuildNeutralBaseCommand: player ou neutralBaseAsset est null.");
            CommandExecutionComplete();
            return;
        }

        player.MainRessourceAvailable -= neutralBaseAsset.mainRessourceBuildingCost;
        player.SecondRessourceAvailable -= neutralBaseAsset.secondRessourceBuildingCost;
        Debug.Log("Player :" + player.name + " has build a base.");
        new UpdateRessourcesCommand(player, player.mainRessourceTotal, player.MainRessourceAvailable, player.secondRessourceTotal, player.SecondRessourceAvailable).AddToQueue();

        CommandExecutionComplete();

    }
}
