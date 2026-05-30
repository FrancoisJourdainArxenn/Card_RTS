using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;


[DefaultExecutionOrder(-100)]
public class NeutralBaseVisual : MonoBehaviour {

    public static readonly Dictionary<int, NeutralBaseVisual> Registry = new();

    public BaseAsset baseAsset;
    public NeutralZoneController neutralBaseController;
    public AreaPosition owner;
    public TMP_Text BaseCostText;
    public TMP_Text MainRessourceIncomeText;
    public GameObject Glow;
    public Transform baseParent;

    [HideInInspector] public Transform BaseApparitionPosition;
    public Transform BasePosition;
    public GameObject BaseCardPrefab;
    private bool canBuild = true;
    private Player localPlayer;


    private int neutralBaseId;
    public int NeutralBaseId { get; private set; }
    public void SetId(int id) => neutralBaseId = id;

    void Awake()
	{
		if(baseAsset != null)
			ApplyLookFromAsset();
        canBuild = true;
	}
    void Start()
    {
        NeutralBaseId = neutralBaseId;
        Registry[NeutralBaseId] = this;
        // Debug.Log($"[NeutralBaseVisual] {gameObject.name} — NeutralBaseId = {NeutralBaseId}");

        if (BaseApparitionPosition == null)
            BaseApparitionPosition = GlobalSettings.Instance.baseApparitionPoint.transform;
    }

    void OnDestroy()
    {
        Registry.Remove(NeutralBaseId);
    }
	
	public void ApplyLookFromAsset()
    {
        BaseCostText.text = baseAsset.mainRessourceBaseCost.ToString();
        MainRessourceIncomeText.text = baseAsset.mainRessourceIncome.ToString();
    }

    void OnMouseEnter()
    {
        if (BuildingShopVisual.IsOpen) return;
        localPlayer = GlobalSettings.Instance.localPlayer;
        bool hasEnoughRessources = localPlayer.MainRessourceAvailable >= baseAsset.mainRessourceBaseCost;
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
        neutralBaseController.SetOwnerColor(GlobalSettings.Instance.NeutralColor);
        neutralBaseController.ResetBuildingSpotTag();
        canBuild = true;
    }

}
