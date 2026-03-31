using UnityEngine;

public class BuildNeutralBaseCommand : Command
{
    private Player player;
    private BaseAsset neutralBaseAsset;

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

        bool hasEnoughRessources = player.MainRessourceAvailable >= neutralBaseAsset.mainRessourceBuildingCost && player.SecondRessourceAvailable >= neutralBaseAsset.secondRessourceBuildingCost;
        if (!hasEnoughRessources)
        {
            ShowMessageCommand showMessageCommand = new ShowMessageCommand("Insufficient Ressources", 2f);
            Debug.Log("ShowMessageCommand: Insufficient Ressources");
            showMessageCommand.AddToQueue();    
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
