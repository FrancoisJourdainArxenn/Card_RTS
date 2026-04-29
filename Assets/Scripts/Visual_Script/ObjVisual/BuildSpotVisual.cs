using UnityEngine;
using System.Collections.Generic;

public class BuildSpotVisual : MonoBehaviour
{
    public static Dictionary<int, BuildSpotVisual> Registry = new Dictionary<int, BuildSpotVisual>();    [SerializeField] private int spotID;
    public int SpotID => spotID;
    [SerializeField] private Transform spawner;
    [SerializeField] private GameObject spotVisual;
    private string originalTag;
    public OneBuildingManager PendingBuilding { get; private set; }



    public ZoneView Zone => _ZoneView;
    private ZoneView _ZoneView;

    void Awake()
    {
        spotID = GetHierarchyPath(transform).GetHashCode();
        Registry[SpotID] = this;
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    void Start()
    {
        _ZoneView = GetComponentInParent<ZoneView>();
        this.tag = _ZoneView.tag;
        originalTag = this.tag;
    }

    public void TakePlayerTag(string playerTag)
    {
        tag = playerTag;
    }

    public void ResetTag()
    {
        tag = originalTag;
    }

    public void ShowBuildings()
    {
        if (TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
        {
            new ShowMessageCommand("You can't build now.", 1.5f).AddToQueue();
            Debug.Log("Coucou");
            return;
        }
        Player localP = GlobalSettings.Instance.localPlayer;

        bool ownsSpot = localP.tag == tag;
        bool controlsZone = PlayerHasUnitsInZone(localP, _ZoneView)
                         && !PlayerHasUnitsInZone(localP.otherPlayer, _ZoneView);

        if(tag == localP.otherPlayer.tag)
        {
            new ShowMessageCommand("You can't build here.", 1.5f).AddToQueue();
            return;
        }
        if (ownsSpot || controlsZone)
            localP.ShowBuildings(this);
        else new ShowMessageCommand("Impossible to build.", 1.5f).AddToQueue();

    }

    private bool PlayerHasUnitsInZone(Player player, ZoneView zone)
    {
        PlayerArea playerAreaInZone = null;
        foreach (PlayerArea pa in player.PAreas)
        {
            if (pa.parentZone == zone)
            {
                playerAreaInZone = pa;
                break;
            }
        }

        if (playerAreaInZone == null) return false;

        foreach (CreatureLogic c in player.playedCards.Creatures)
        {
            if (c.BaseID == playerAreaInZone.baseID)
                return true;
        }
        return false;
    }
    public void SpawnPendingBuilding(CardAsset building, Player owner)
    {
        GameObject buildingGO = Instantiate(GlobalSettings.Instance.BuildingPrefab, spawner.transform.position, Quaternion.identity);
        buildingGO.tag = owner.tag;

        OneBuildingManager manager = buildingGO.GetComponent<OneBuildingManager>();
        manager.cardAsset = building;
        manager.OriginSpot = this;
        manager.ReadBuidingFromAsset();
        manager.SetPending(true);

        PendingBuilding = manager;
        spotVisual.SetActive(false);
    }

    public void SpawnBuilding(BuildingLogic buildingLogic, Player owner)
    {
        OneBuildingManager manager;

        if (PendingBuilding != null)
        {
            manager = PendingBuilding;
            PendingBuilding = null;

            IDHolder idHolder = manager.gameObject.GetComponent<IDHolder>();
            if (idHolder == null) idHolder = manager.gameObject.AddComponent<IDHolder>();
            idHolder.UniqueID = buildingLogic.UniqueBuildingID;

            manager.BuildingLogic = buildingLogic;
            manager.SetPending(false);
        }
        else
        {
            GameObject buildingGO = Instantiate(GlobalSettings.Instance.BuildingPrefab, spawner.transform.position, Quaternion.identity);
            buildingGO.tag = owner.tag;

            IDHolder idHolder = buildingGO.GetComponent<IDHolder>();
            if (idHolder == null) idHolder = buildingGO.AddComponent<IDHolder>();
            idHolder.UniqueID = buildingLogic.UniqueBuildingID;

            manager = buildingGO.GetComponent<OneBuildingManager>();
            manager.cardAsset = buildingLogic.ca;
            manager.BuildingLogic = buildingLogic;
            manager.OriginSpot = this;
            manager.ReadBuidingFromAsset();
            spotVisual.SetActive(false);
        }
    }

    public void OnBuildingDestroyed()
    {
        spotVisual.SetActive(true);
    }


}
