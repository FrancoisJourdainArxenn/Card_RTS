public class BuildNeutralBaseCommand : Command
{
    private Player player;
    private NeutralBaseVisual spawner;
    private int buildingUniqueID;

    public BuildNeutralBaseCommand(int buildingUniqueID, Player player, NeutralBaseVisual spawner)
    {
        this.player = player;
        this.buildingUniqueID = buildingUniqueID;
        this.spawner = spawner;
    }

    public override void StartCommandExecution()
    {
        if (player == null || spawner == null)
        {
            CommandExecutionComplete();
            return;
        }

        BaseAsset ba = spawner.baseAsset;
        player.MainRessourceAvailable -= ba.mainRessourceBuildingCost;
        player.SecondRessourceAvailable -= ba.secondRessourceBuildingCost;
        new UpdateRessourcesCommand(player, player.mainRessourceTotal, player.MainRessourceAvailable, player.secondRessourceTotal, player.SecondRessourceAvailable).AddToQueue();
        spawner.neutralBaseController.AddBase(ba, buildingUniqueID, player, spawner);
        CommandExecutionComplete();
    }
}
