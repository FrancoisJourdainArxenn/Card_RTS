using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using DG.Tweening;

public class NeutralBaseController : MonoBehaviour
{
    public AreaPosition owner;
    public Color ownerColor;
    public GameObject background;
    public ZoneLogic zone;

    public TableVisual[] tables;

    private List<GameObject> buildings = new List<GameObject>();

    public void SetOwnerColor(Color color)
    {
        ownerColor = color;
        background.GetComponent<Image>().color = ownerColor;
    }

    public void AddBase(BaseAsset ba, int buildingUniqueID, Player player, NeutralBaseVisual nBaseVisual)
    {
        GameObject baseCard = Instantiate(nBaseVisual.BaseCardPrefab, nBaseVisual.BaseApparitionPosition.position, nBaseVisual.BaseApparitionPosition.rotation);
        buildings.Add(baseCard);
        OneBuildingManager baseManager = baseCard.GetComponent<OneBuildingManager>();
        baseManager.baseAsset = ba;
        baseManager.ResetValues(ba);
        baseManager.Spawner = nBaseVisual.gameObject;
        baseCard.tag = player.tag;

        IDHolder idHolder = baseCard.GetComponent<IDHolder>();
        idHolder.UniqueID = buildingUniqueID;
        player.controlledBases.Add(ba);
        nBaseVisual.BuildingZone.GetComponent<Image>().color = player.playerColor;
        player.CalculatePlayerIncome();
        nBaseVisual.RemoveBaseCard();

        // Animate scale (pop-in)
        baseCard.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);

        // Animate movement from BaseApparitionPosition to BasePosition
        if (nBaseVisual.BasePosition != null)
        {
            baseCard.transform.DOMove(nBaseVisual.BasePosition.position, 0.7f).SetEase(Ease.InOutQuad);
            baseCard.transform.DORotateQuaternion(nBaseVisual.BasePosition.rotation, 0.7f).SetEase(Ease.InOutQuad);
        }
    }

    public void RemoveBuildingWithID(int buildingUniqueID)
    {
        GameObject buildingToRemove = IDHolder.GetGameObjectWithID(buildingUniqueID);
        if (buildingToRemove == null)
            return;

        buildings.Remove(buildingToRemove);

        // Carte de base : OneBuildingManager sur la racine (pas BaseVisual — celui-ci est pour le portrait héros).
        OneBuildingManager mgr = buildingToRemove.GetComponent<OneBuildingManager>();
        if (mgr != null)
        {
            if (mgr.Spawner != null)
            {
                mgr.Spawner.SetActive(true);
                NeutralBaseVisual nBaseVisual = mgr.Spawner.GetComponent<NeutralBaseVisual>();
                if (nBaseVisual != null)
                    nBaseVisual.ResetBuildingZone();
            }
            Object.Destroy(buildingToRemove);
        }
        else
            Object.Destroy(buildingToRemove);
    }

    public void SetEnemyBuildingsFogged(Player enemy, bool fogged)
    {
        foreach (GameObject building in buildings)
        {
            if (building != null && building.CompareTag(enemy.tag))
                building.SetActive(!fogged);
        }
    }

}
