public class RefreshHandStatsCommand : Command
{
    private Player player;
    public RefreshHandStatsCommand(Player p) { player = p; }

    public override void StartCommandExecution()
    {
        if (player.handVisual != null)
            player.handVisual.RefreshPermanentBuffStats();
        CommandExecutionComplete();
    }
}
