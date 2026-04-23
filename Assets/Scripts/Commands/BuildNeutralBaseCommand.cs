public class BuildNeutralBaseCommand : Command
{
    private Player player;
    private NeutralBaseVisual spawner;
    private int baseUniqueID;

    public BuildNeutralBaseCommand(int baseUniqueID, Player player, NeutralBaseVisual spawner)
    {
        this.player = player;
        this.baseUniqueID = baseUniqueID;
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
        player.MainRessourceAvailable -= ba.mainRessourceBaseCost;
        player.SecondRessourceAvailable -= ba.secondRessourceBaseCost;
        new UpdateRessourcesCommand(player, player.mainRessourceTotal, player.MainRessourceAvailable, player.secondRessourceTotal, player.SecondRessourceAvailable).AddToQueue();
        spawner.neutralBaseController.AddBase(ba, baseUniqueID, player, spawner);
        CommandExecutionComplete();
    }
}
