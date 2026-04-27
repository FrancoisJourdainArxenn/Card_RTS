public class PlaceBuildingCommand : Command
{
    private CardAsset building;
    private Player player;
    private BuildSpotVisual spot;
    private int buildingUniqueID;
    private bool alreadyPaid;

    public PlaceBuildingCommand(CardAsset building, Player player, BuildSpotVisual spot, int buildingUniqueID, bool alreadyPaid = false)
    {
        this.building = building;
        this.player = player;
        this.spot = spot;
        this.buildingUniqueID = buildingUniqueID;
        this.alreadyPaid = alreadyPaid;
    }

    public override void StartCommandExecution()
    {
        if (player == null || spot == null || building == null)
        {
            CommandExecutionComplete();
            return;
        }

        if (!alreadyPaid)
        {
            player.MainRessourceAvailable -= building.MainCost;
            player.SecondRessourceAvailable -= building.SecondCost;
            new UpdateRessourcesCommand(player, player.mainRessourceTotal, player.MainRessourceAvailable, player.secondRessourceTotal, player.SecondRessourceAvailable).AddToQueue();
        }

        BuildingLogic buildingLogic = new BuildingLogic(player, building, spot, buildingUniqueID);
        buildingLogic.OnTurnStart();
        player.table.BuildingsInPlay.Add(buildingLogic);
        spot.SpawnBuilding(buildingLogic, player);
        if (spot.Zone != null)
            foreach (PlayerArea pa in spot.Zone.subZones)
                pa.RefreshAreaStats();

        FogOfWarManager.Refresh();
        CommandExecutionComplete();
    }
}
