using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;


public class NeutralBaseVisual : MonoBehaviour {

    public BaseAsset baseAsset;
    public AreaPosition owner;
    public TMP_Text BuildingCostText;
    public TMP_Text MainRessourceIncomeText;
    public TMP_Text SecondRessourceIncomeText;
    public GameObject Glow;
    public GameObject BuildingZone;

    public Transform BaseApparitionPosition;
    public Transform BasePosition;
    public GameObject BaseCardPrefab;

    private bool canBuild = true;
    private Player activePlayer;
    
    void Awake()
	{
		if(baseAsset != null)
			ApplyLookFromAsset();
        canBuild = true;

	}
	
	public void ApplyLookFromAsset()
    {
        BuildingCostText.text = baseAsset.mainRessourceBuildingCost.ToString();
        MainRessourceIncomeText.text = baseAsset.mainRessourceIncome.ToString();
        SecondRessourceIncomeText.text = baseAsset.secondRessourceIncome.ToString();
    }

    void OnMouseEnter()
    {
        activePlayer = GlobalSettings.Instance.activePlayer;
        bool hasEnoughRessources = activePlayer.MainRessourceAvailable >= baseAsset.mainRessourceBuildingCost && activePlayer.SecondRessourceAvailable >= baseAsset.secondRessourceBuildingCost;
        Glow.GetComponent<Image>().color = hasEnoughRessources ? Color.green : Color.red;
        Glow.SetActive(true);
    }

    void OnMouseDown()
    {
        activePlayer.CreateANewNeutralBase(baseAsset, this);
        RemoveBaseCard();
    }
    void OnMouseExit()
    {
        Glow.SetActive(false);
    }

    public void InstantiateBaseCard(Player player, int buildingUniqueID)
    {
        GameObject baseCard = Instantiate(BaseCardPrefab, BaseApparitionPosition.position, BaseApparitionPosition.rotation);
        
        OneBuildingManager baseManager = baseCard.GetComponent<OneBuildingManager>();
        baseManager.baseAsset = baseAsset;
        baseManager.ResetValues(baseAsset);
        baseManager.Spawner = this.gameObject;
        baseCard.tag = player.tag;
        Debug.Log("Player tag :" + player.tag);

        IDHolder idHolder = baseCard.GetComponent<IDHolder>();
        idHolder.UniqueID = buildingUniqueID;
        player.controlledBases.Add(baseAsset);
        BuildingZone.GetComponent<Image>().color = player.playerColor;
        player.CalculatePlayerIncome();
        RemoveBaseCard();

        // Animate scale (pop-in)
        baseCard.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);

        // Animate movement from BaseApparitionPosition to BasePosition
        if (BasePosition != null)
        {
            baseCard.transform.DOMove(BasePosition.position, 0.7f).SetEase(Ease.InOutQuad);
            baseCard.transform.DORotateQuaternion(BasePosition.rotation, 0.7f).SetEase(Ease.InOutQuad);
        }

    }

    public void RemoveBaseCard()
    {
        gameObject.SetActive(false);
    }

    public void ResetBuildingZone()
    {
        BuildingZone.GetComponent<Image>().color = GlobalSettings.Instance.NeutralColor;
        canBuild = true;
    }
}
