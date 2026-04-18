using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;


[DefaultExecutionOrder(-100)]
public class NeutralBaseVisual : MonoBehaviour {

    public static readonly Dictionary<int, NeutralBaseVisual> Registry = new();

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

    [SerializeField] private int neutralBaseId;
    public int NeutralBaseId { get; private set; }

    void Awake()
	{
        NeutralBaseId = neutralBaseId;
        Registry[NeutralBaseId] = this;

		if(baseAsset != null)
			ApplyLookFromAsset();
        canBuild = true;
	}

    void OnDestroy()
    {
        Registry.Remove(NeutralBaseId);
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
            localPlayer.RequestBuildNeutralBase(NeutralBaseId);
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
