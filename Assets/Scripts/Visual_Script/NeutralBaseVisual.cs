using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;


public class NeutralBaseVisual : MonoBehaviour {

    public BaseAsset baseAsset;
    public NeutralBaseController neutralBaseController;
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
    private Player localPlayer;


    static string ComputePath(Transform t) =>
        t.parent == null ? t.name : ComputePath(t.parent) + "/" + t.name;

    
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
        localPlayer = GlobalSettings.Instance.localPlayer;
        bool hasEnoughRessources = localPlayer.MainRessourceAvailable >= baseAsset.mainRessourceBuildingCost && localPlayer.SecondRessourceAvailable >= baseAsset.secondRessourceBuildingCost;
        Glow.GetComponent<Image>().color = hasEnoughRessources ? Color.green : Color.red;
        Glow.SetActive(true);
    }

    void OnMouseDown()
    {
        canBuild = localPlayer.CheckIfCanBuild(baseAsset, neutralBaseController);
        if (canBuild)
            localPlayer.CreateANewNeutralBase(baseAsset, this, neutralBaseController);
    }

    void OnMouseExit()
    {
        Glow.SetActive(false);
    }

    public void RemoveBaseCard()
    {
        gameObject.SetActive(false);
    }

    public void ResetBuildingZone()
    {
        // BuildingZone.GetComponent<Image>().color = GlobalSettings.Instance.NeutralColor;
        neutralBaseController.SetTrueColor(GlobalSettings.Instance.NeutralColor);
        canBuild = true;

    }
}
