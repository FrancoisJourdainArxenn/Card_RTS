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
    
    void Awake()
	{
		if(baseAsset != null)
			ApplyLookFromAsset();
	}
	
	public void ApplyLookFromAsset()
    {
        BuildingCostText.text = baseAsset.mainRessourceBuildingCost.ToString();
        MainRessourceIncomeText.text = baseAsset.mainRessourceIncome.ToString();
        SecondRessourceIncomeText.text = baseAsset.secondRessourceIncome.ToString();
    }

    void OnMouseEnter()
    {
        Player activePlayer = GlobalSettings.Instance.activePlayer;
        bool hasEnoughRessources = activePlayer.MainRessourceAvailable >= baseAsset.mainRessourceBuildingCost && activePlayer.SecondRessourceAvailable >= baseAsset.secondRessourceBuildingCost;
        Glow.GetComponent<Image>().color = hasEnoughRessources ? Color.green : Color.red;
        Glow.SetActive(true);
    }

    void OnMouseDown()
    {
        new BuildNeutralBaseCommand(GlobalSettings.Instance.activePlayer, baseAsset).AddToQueue(); //active player ne change pas à l'heure actuelle.
    }

    void OnMouseExit()
    {
        Glow.SetActive(false);
    }



}
