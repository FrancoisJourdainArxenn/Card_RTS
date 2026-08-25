using UnityEngine;
using System.Collections.Generic;

public class BuildSpotVisual : MonoBehaviour
{
    public static Dictionary<int, BuildSpotVisual> Registry = new Dictionary<int, BuildSpotVisual>();    [SerializeField] private int spotID;
    public int SpotID => spotID;
    [SerializeField] private Transform spawner;
    [SerializeField] private GameObject spotVisual;
    [SerializeField] private GameObject buildTextObject;
    private string originalTag;
    public OneBuildingManager PendingBuilding { get; private set; }



    public ZoneManager Zone => _ZoneView;
    private ZoneManager _ZoneView;

    void Awake()
    {
        // Animator.StringToHash (CRC32) au lieu de string.GetHashCode() — voir ZoneManager.Awake()
        // pour la même correction et son explication (hash de string randomisé par processus).
        spotID = Animator.StringToHash(GetHierarchyPath(transform));
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
        _ZoneView = GetComponentInParent<ZoneManager>();
        this.tag = _ZoneView.tag;
        originalTag = this.tag;
        RefreshBuildLabel();
    }

    void OnEnable()
    {
        TurnManager.OnPhaseEntered += RefreshBuildLabel;
        if (_ZoneView != null) RefreshBuildLabel();
    }

    void OnDisable()
    {
        TurnManager.OnPhaseEntered -= RefreshBuildLabel;
    }

    void OnDestroy()
    {
        Registry.Remove(SpotID);
    }

    public void TakePlayerTag(string playerTag)
    {
        tag = playerTag;
        RefreshBuildLabel();
    }

    public void ResetTag()
    {
        tag = originalTag;
        RefreshBuildLabel();
    }

    private bool CanBuild()
    {
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command)
            return false;

        if (GlobalSettings.Instance == null) return false;
        Player localP = GlobalSettings.Instance.localPlayer;
        if (localP == null || localP.otherPlayer == null) return false;

        if (tag == localP.otherPlayer.tag)
            return false;

        bool ownsSpot = localP.tag == tag;
        bool controlsZone = PlayerHasUnitsInZone(localP, _ZoneView)
                        && !PlayerHasUnitsInZone(localP.otherPlayer, _ZoneView);

        return ownsSpot || controlsZone;
    }


    public void ShowBuildings()
    {
        if (!CanBuild())
        {
            string msg = TurnManager.Instance.CurrentPhase != TurnManager.TurnPhases.Command
                ? "You can't build now."
                : tag == GlobalSettings.Instance.localPlayer.otherPlayer.tag
                    ? "You don't own this base."
                    : "Impossible to build.";
            new ShowMessageCommand(msg, 1.5f).AddToQueue();
            return;
        }
        GlobalSettings.Instance.localPlayer.ShowBuildings(this);
    }

    public void RefreshBuildLabel()
    {
        if (buildTextObject != null)
            buildTextObject.gameObject.SetActive(CanBuild());
    }

    public static void RefreshAll()
    {
        foreach (BuildSpotVisual spot in Registry.Values)
            spot.RefreshBuildLabel();
    }

    private bool PlayerHasUnitsInZone(Player player, ZoneManager zone)
    {
        foreach (PlayerArea pa in zone.subZones)
        {
            foreach (CreatureLogic c in player.playedCards.Creatures)
                if (c.BaseID == pa.baseID) return true;

        }
        return false;
    }
    
    public void SpawnPendingBuilding(CardAsset building, Player owner)
    {
        GameObject buildingGO = Instantiate(GlobalSettings.Instance.BuildingPrefab, spawner.transform.position, Quaternion.identity);
        buildingGO.transform.SetParent(transform, true);
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
            buildingGO.transform.SetParent(transform, true);
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
            HoverPreview hover = manager.GetComponentInChildren<HoverPreview>();
            if (hover != null) hover.ThisPreviewEnabled = true;
    }

    public void OnBuildingDestroyed()
    {
        spotVisual.SetActive(true);
    }


}
