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
        activePlayer = GlobalSettings.Instance.activePlayer;
        if(TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Command)
        {
            if(canBuild && activePlayer.MainRessourceAvailable >= baseAsset.mainRessourceBuildingCost && activePlayer.SecondRessourceAvailable >= baseAsset.secondRessourceBuildingCost)
            {
                new BuildNeutralBaseCommand(activePlayer, baseAsset).AddToQueue(); //active player ne change pas à l'heure actuelle.
                canBuild = false;
                InstantiateBaseCard();
            }
            else
            {
                ShowMessageCommand showMessageCommand = new ShowMessageCommand("Insufficient Ressources", 2f);
                Debug.Log("ShowMessageCommand: Insufficient Ressources");
                showMessageCommand.AddToQueue();
            }
        }
        else
        {
            ShowMessageCommand showMessageCommand = new ShowMessageCommand("You can't do that right now", 2f);
            Debug.Log("ShowMessageCommand: Not your turn");
            showMessageCommand.AddToQueue();
        }
    }

    void OnMouseExit()
    {
        Glow.SetActive(false);
    }

    public void InstantiateBaseCard()
    {
        GameObject baseCard = Instantiate(BaseCardPrefab, BaseApparitionPosition.position, BaseApparitionPosition.rotation);
        
        OneBaseManager baseManager = baseCard.GetComponent<OneBaseManager>();
        baseManager.baseAsset = baseAsset;
        baseManager.ResetValues(baseAsset);
        baseManager.Spawner = this.gameObject;
        IDHolder idHolder = baseCard.GetComponent<IDHolder>();
        idHolder.UniqueID = IDFactory.GetUniqueID();

        if(activePlayer.MainPArea.owner == AreaPosition.Top)
        {
            baseCard.tag = "TopPlayer";
            BuildingZone.GetComponent<Image>().color = activePlayer.playerColor;
            activePlayer.controlledBases.Add(baseAsset);
            activePlayer.CalculatePlayerIncome();
            RemoveBaseCard();
        }
        else
        {
            baseCard.tag = "LowPlayer";
            BuildingZone.GetComponent<Image>().color = activePlayer.playerColor;
            activePlayer.controlledBases.Add(baseAsset);
            activePlayer.CalculatePlayerIncome();
            RemoveBaseCard();
        }

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
