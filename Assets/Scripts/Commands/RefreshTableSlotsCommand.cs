public class RefreshTableSlotsCommand : Command
{
    private TableVisual tableVisual;
    public RefreshTableSlotsCommand(TableVisual tv) { tableVisual = tv; }

    public override void StartCommandExecution()
    {
        tableVisual.RefreshSlotsPositions();
        CommandExecutionComplete();
    }
}
