using UnityEngine;

public class BuildSpotVisual : MonoBehaviour
{
    private ZoneLogic _zoneLogic;

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
            localP.ShowBuildings();
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
}
