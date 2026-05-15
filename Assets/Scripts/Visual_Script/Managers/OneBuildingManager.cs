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
        if (TurnManager.Instance.IsBattlePhase)
        {
            ZoneCombatResolver resolver = ZoneCombatResolver.FindForBuilding(BuildingLogic);
            if (resolver != null)
                resolver.TryRedirectDamageFromBuilding(BuildingLogic);
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

    public void UpdateGlow()
    {
        if(TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Battle)
            glow.color = Color.red;
        if(TurnManager.Instance.CurrentPhase == TurnManager.TurnPhases.Command)
            glow.color = Color.green;
        glow.enabled = CanAttackNow;
    }

    public void ShowPendingDamage(int damage, int currentHealth)
    {
        bool dies = damage >= currentHealth;
        if (MarkedForDeathIndicator != null) MarkedForDeathIndicator.SetActive(dies);
        if (WillBeDamagedIndicator != null)
        {
            WillBeDamagedIndicator.SetActive(!dies);
            if (!dies && pendingDamageText != null)
                pendingDamageText.text = damage.ToString();
        }
    }

    public void ClearPendingDamageIndicator()
    {
        if (MarkedForDeathIndicator != null) MarkedForDeathIndicator.SetActive(false);
        if (WillBeDamagedIndicator != null)  WillBeDamagedIndicator.SetActive(false);
    }

    public void SetPending(bool isPending)
    {
        art.color = isPending ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
        if (PendingIcon != null) PendingIcon.SetActive(isPending);
    }

}