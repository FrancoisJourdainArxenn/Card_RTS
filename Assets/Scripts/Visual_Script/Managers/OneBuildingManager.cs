using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;

public class OneBuildingManager : OneLivableManager 
{
    public GameObject PendingIcon;
    public GameObject AttackDamageBG;

    public BuildingLogic BuildingLogic { get; set; }
    public BuildSpotVisual OriginSpot { get; set; }


    void Awake()
    {
        if (cardAsset != null)
            ReadBuidingFromAsset();
    }

    public void OnBuildingClicked()
    {
        if (TurnManager.Instance == null) return;
        if (BuildingLogic == null) return;

        if (!PhaseEffectPipeline.IsPlayerTargetingComplete(GlobalSettings.Instance.localPlayer))
        {
            IDHolder idHolder = GetComponent<IDHolder>();
            if (idHolder == null) return;
            if (!BuildingLogic.BuildingsCreatedThisGame.TryGetValue(idHolder.UniqueID, out BuildingLogic building)) return;
            if (PhaseEffectPipeline.OnEntityClicked(building))
                return; // consumed by targeting
            // Not eligible — fall through
        }
    }

    public void ReadBuidingFromAsset()
    {
        // Change the card graphic sprite
        art.sprite = cardAsset.CardImage;
        HealthText.text = cardAsset.MaxHealth.ToString();
        if(cardAsset.Attack > 0)
        {
            AttackDamageBG.SetActive(true);
            AttackText.text = cardAsset.Attack.ToString();
        }

        if(cardAsset.melee)
        {
            MeleeImage.enabled = true;
        }

        if (PreviewManager != null)
        {
            PreviewManager.cardAsset = cardAsset;
            PreviewManager.ReadCardFromAsset();
        }
        
    }

    public void SetPending(bool isPending)
    {
        art.color = isPending ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
        if (PendingIcon != null) PendingIcon.SetActive(isPending);
    }

}