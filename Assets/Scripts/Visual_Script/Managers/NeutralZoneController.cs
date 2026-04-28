using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using DG.Tweening;

public class NeutralZoneController : MonoBehaviour
{
    public AreaPosition owner;
    public Color ownerColor;
    public GameObject background;
    public ZoneLogic zone;

    public TableVisual[] tables;

    private List<GameObject> bases = new List<GameObject>();
    private NeutralBaseVisual capturedNBaseVisual = null;
    private Color trueColor;
    private Color lastSeenColorLow;
    private Color lastSeenColorTop;


    void Awake()
    {
        Image bg = background != null ? background.GetComponent<Image>() : null;
        Color initial = bg != null ? bg.color : Color.white;
        trueColor = initial;
        lastSeenColorLow = initial;
        lastSeenColorTop = initial;

        tables = GetComponentsInChildren<TableVisual>();

    }

    public void SetTrueColor(Color color)
    {
        trueColor = color;
        ownerColor = color;
        FogOfWarManager.Refresh();
    }

    public void ApplyColorForObserver(Player observer, bool hasVision)
    {
        bool isLow = observer == GlobalSettings.Instance.LowPlayer;
        if (hasVision)
        {
            if (isLow) lastSeenColorLow = trueColor;
            else lastSeenColorTop = trueColor;
            background.GetComponent<Image>().color = trueColor;
        }
        else
        {
            background.GetComponent<Image>().color = isLow ? lastSeenColorLow : lastSeenColorTop;
        }
    }


    public void SetOwnerColor(Color color)
    {
        ownerColor = color;
        background.GetComponent<Image>().color = ownerColor;
    }

    public void AddBase(BaseAsset ba, int baseUniqueID, Player player, NeutralBaseVisual nBaseVisual)
    {
        GameObject baseCard = Instantiate(nBaseVisual.BaseCardPrefab, nBaseVisual.BaseApparitionPosition.position, nBaseVisual.BaseApparitionPosition.rotation);
        if (nBaseVisual.baseParent != null)
            baseCard.transform.SetParent(nBaseVisual.baseParent, true);
        bases.Add(baseCard);
        OneBaseManager baseManager = baseCard.GetComponent<OneBaseManager>();
        baseManager.baseAsset = ba;
        baseManager.ResetValues(ba);
        baseManager.Spawner = nBaseVisual.gameObject;
        baseCard.tag = player.tag;
        SetBuildingSpotTag(player.tag);

        IDHolder idHolder = baseCard.GetComponent<IDHolder>();
        idHolder.UniqueID = baseUniqueID;
        player.controlledBaseAssets.Add(ba);
        // nBaseVisual.BuildingZone.GetComponent<Image>().color = player.playerColor;
        capturedNBaseVisual = nBaseVisual;
        SetTrueColor(player.playerColor); // triggers FogOfWarManager.Refresh() which will hide/show NeutralBaseVisual per observer
        player.CalculatePlayerIncome();

        // Animate scale (pop-in)
        baseCard.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);

        // Animate movement from BaseApparitionPosition to BasePosition
        if (nBaseVisual.BasePosition != null)
        {
            baseCard.transform.DOMove(nBaseVisual.BasePosition.position, 0.7f).SetEase(Ease.InOutQuad);
            baseCard.transform.DORotateQuaternion(Quaternion.identity, 0.7f).SetEase(Ease.InOutQuad);
        }
    }

    public void RemoveBaseWithID(int baseUniqueID)
    {
        GameObject baseToRemove = IDHolder.GetGameObjectWithID(baseUniqueID);
        if (baseToRemove == null)
            return;

        bases.Remove(baseToRemove);

        // Carte de base : OneBaseManager sur la racine (pas MainBaseVisual — celui-ci est pour le portrait héros).
        OneBaseManager mgr = baseToRemove.GetComponent<OneBaseManager>();
        if (mgr != null)
        {
            capturedNBaseVisual = null;
            if (mgr.Spawner != null)
            {
                mgr.Spawner.SetActive(true);
                NeutralBaseVisual nBaseVisual = mgr.Spawner.GetComponent<NeutralBaseVisual>();
                if (nBaseVisual != null)
                    nBaseVisual.ResetBuildingZone();
            }

            Object.Destroy(baseToRemove);
        }
        else
            Object.Destroy(baseToRemove);
    }

    public void UpdateNeutralBaseVisualFog(bool observerHasVision)
    {
        if (capturedNBaseVisual != null)
            capturedNBaseVisual.gameObject.SetActive(!observerHasVision);
    }

    public void SetEnemyBasesFogged(Player enemy, bool fogged)
    {
        foreach (GameObject _base in bases)
        {
            if (_base != null && _base.CompareTag(enemy.tag))
                _base.SetActive(!fogged);
        }
    }

    public void SetPlayerBasesVisible(Player player)
    {
        foreach (GameObject _base in bases)
        {
            if (_base != null && _base.CompareTag(player.tag))
                _base.SetActive(true);
        }
    }

    public void SetBuildingSpotTag(string playerTag)
    {
        foreach (BuildSpotVisual spot in GetComponentsInChildren<BuildSpotVisual>())
            spot.TakePlayerTag(playerTag);
    }

    public void ResetBuildingSpotTag()
    {
        foreach (BuildSpotVisual spot in GetComponentsInChildren<BuildSpotVisual>())
            spot.ResetTag();
    }



}
