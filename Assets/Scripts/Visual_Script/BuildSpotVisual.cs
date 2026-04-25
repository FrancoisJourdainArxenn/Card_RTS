using UnityEngine;
using System.Collections.Generic;

public class BuildSpotVisual : MonoBehaviour
{
    public static Dictionary<int, BuildSpotVisual> Registry = new Dictionary<int, BuildSpotVisual>();    [SerializeField] private int spotID;
    public int SpotID => spotID;
    [SerializeField] private Transform spawner;
    [SerializeField] private GameObject spotVisual;



    private ZoneLogic _zoneLogic;

    void Awake()
    {
        spotID = IDFactory.GetUniqueID();
        Registry[SpotID] = this;
    }

    void Start()
    {
        _zoneLogic = GetComponentInParent<ZoneLogic>();
        this.tag = _zoneLogic.tag;
    }

    public void TakePlayerTag(string playerTag)
    {
        tag = playerTag;
    }

    public void ShowBuildings()
    {
        Player localP = GlobalSettings.Instance.localPlayer;

        bool ownsSpot = localP.tag == tag;
        bool controlsZone = PlayerHasUnitsInZone(localP, _zoneLogic)
                         && !PlayerHasUnitsInZone(localP.otherPlayer, _zoneLogic);

        if (ownsSpot || controlsZone)
            localP.ShowBuildings(this);
        else new ShowMessageCommand("You can't build here.", 1.5f);
    }

    private bool PlayerHasUnitsInZone(Player player, ZoneLogic zone)
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

        foreach (CreatureLogic c in player.table.CreaturesInPlay)
        {
            if (c.BaseID == playerAreaInZone.baseID)
                return true;
        }
        return false;
    }

    public void SpawnBuilding(BuildingLogic buildingLogic, Player owner)
    {
        GameObject buildingPrefab = GlobalSettings.Instance.BuildingPrefab;
        GameObject buildingGO = Instantiate(buildingPrefab, spawner.transform.position, Quaternion.identity);
        buildingGO.tag = owner.tag;

        IDHolder idHolder = buildingGO.GetComponent<IDHolder>();
        if (idHolder == null) idHolder = buildingGO.AddComponent<IDHolder>();
        idHolder.UniqueID = buildingLogic.UniqueBuildingID;

        OneBuildingManager manager = buildingGO.GetComponent<OneBuildingManager>();
        manager.cardAsset = buildingLogic.ca;
        manager.BuildingLogic = buildingLogic;
        manager.OriginSpot = this;

        manager.ReadBuidingFromAsset();
        spotVisual.SetActive(false);
    }

    public void OnBuildingDestroyed()
    {
        spotVisual.SetActive(true);
    }


}
