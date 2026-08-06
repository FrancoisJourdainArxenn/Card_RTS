using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using DG.Tweening;

public class NeutralZoneController : MonoBehaviour
{
    public AreaPosition owner;
    public Color ownerColor;
    public GameObject background;
    public ZoneManager zone;

    public TableVisual[] tables;

    private List<GameObject> bases = new List<GameObject>();
    private Dictionary<GameObject, GameObject> _baseGhosts = new Dictionary<GameObject, GameObject>();
    private NeutralBaseVisual capturedNBaseVisual = null;
    private Color trueColor;
    private Color lastSeenColorLow;
    private Color lastSeenColorTop;
    private Image backgroundImage;


    void Awake()
    {
        backgroundImage = background != null ? background.GetComponent<Image>() : null;
        Color initial = backgroundImage != null ? backgroundImage.color : Color.white;
        trueColor = initial;
        lastSeenColorLow = initial;
        lastSeenColorTop = initial;

        tables = GetComponentsInChildren<TableVisual>();
        if (zone == null) zone = GetComponent<ZoneManager>();
    }

    public void ApplyColorForObserver(Player observer, bool hasVision)
    {
        bool isLow = observer == GlobalSettings.Instance.LowPlayer;
        Color targetColor;
        if (hasVision)
        {
            if (isLow) lastSeenColorLow = trueColor;
            else lastSeenColorTop = trueColor;
            targetColor = trueColor;
        }
        else
        {
            targetColor = isLow ? lastSeenColorLow : lastSeenColorTop;
        }

        bool shouldShow = targetColor != (Color)GlobalSettings.Instance.NeutralColor;
        if (background != null)
            background.SetActive(shouldShow);
        if (backgroundImage != null && backgroundImage.color != targetColor)
            backgroundImage.color = targetColor;
    }


    public void SetOwnerColor(Color color)
    {
        trueColor = color;
        ownerColor = color;
        FogOfWarManager.Refresh();
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
        bool isLowPlayer = player == GlobalSettings.Instance.LowPlayer;
        string playerTag = isLowPlayer ? "LowPlayer" : "TopPlayer";
        SetBuildingSpotTag(playerTag);


        IDHolder idHolder = baseCard.GetComponent<IDHolder>();
        idHolder.UniqueID = baseUniqueID;
        player.controlledBaseAssets.Add(ba);
        // nBaseVisual.BuildingZone.GetComponent<Image>().color = player.playerColor;
        capturedNBaseVisual = nBaseVisual;
        SetOwnerColor(player.playerColor); // triggers FogOfWarManager.Refresh() which will hide/show NeutralBaseVisual per observer
        player.CalculatePlayerIncome();

        // Animate scale (pop-in)
        baseCard.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);

        // Animate movement from BaseApparitionPosition to the resting position matching the capturing player's side
        Transform targetPosition = isLowPlayer ? nBaseVisual.LowBasePosition : nBaseVisual.TopBasePosition;
        if (targetPosition != null)
        {
            baseCard.transform.DOMove(targetPosition.position, 0.7f).SetEase(Ease.InOutQuad);
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
        if (!fogged)
        {
            List<GameObject> orphaned = null;
            foreach (KeyValuePair<GameObject, GameObject> gKvp in _baseGhosts)
            {
                if (gKvp.Key == null)
                {
                    if (orphaned == null) orphaned = new List<GameObject>();
                    orphaned.Add(gKvp.Key);
                }
            }
            if (orphaned != null)
                foreach (GameObject key in orphaned)
                {
                    if (_baseGhosts[key] != null) Destroy(_baseGhosts[key]);
                    _baseGhosts.Remove(key);
                }
        }

        foreach (GameObject _base in bases)
        {
            if (_base == null || !_base.CompareTag(enemy.tag)) continue;

            OneBaseManager mgr = _base.GetComponent<OneBaseManager>();

            if (!fogged)
            {
                mgr?.MarkSeen();
                if (_baseGhosts.TryGetValue(_base, out GameObject existingGhost))
                {
                    if (existingGhost != null) Destroy(existingGhost);
                    _baseGhosts.Remove(_base);
                }
                _base.SetActive(true);
            }
            else
            {
                _base.SetActive(false);

                if (mgr != null && mgr.HasBeenSeen && !_baseGhosts.ContainsKey(_base))
                {
                    GameObject ghost = Instantiate(_base, _base.transform.parent);
                    OneBaseManager ghostMgr = ghost.GetComponent<OneBaseManager>();
                    ghostMgr.SetGray(true);
                    IDHolder ghostId = ghost.GetComponent<IDHolder>();
                    if (ghostId != null) Destroy(ghostId);
                    ghost.SetActive(true);
                    _baseGhosts[_base] = ghost;
                }
            }
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
        foreach (BuildSpotVisual spot in GetComponentsInChildren<BuildSpotVisual>(true))
            spot.TakePlayerTag(playerTag);
    }

    public void ResetBuildingSpotTag()
    {
        foreach (BuildSpotVisual spot in GetComponentsInChildren<BuildSpotVisual>(true))
            spot.ResetTag();
    }



}
