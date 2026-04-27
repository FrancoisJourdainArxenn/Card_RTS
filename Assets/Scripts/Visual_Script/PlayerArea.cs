using UnityEngine;
using System.Collections;
using TMPro;

public enum AreaPosition{Top, Low, Neutral} // Interesting

public class PlayerArea : MonoBehaviour 
{
    public AreaPosition owner;
    public bool ControlsON = true;
    public HandVisual handVisual;
    public TableVisual tableVisual;
    public Transform BasePosition;
    public int baseID;

    [HideInInspector]
    public ZoneLogic parentZone;
    public Transform BattlePos;

    public TMP_Text AreaATKText;
    public TMP_Text AreaHealthText;

    public bool AllowedToControlThisPlayer
    {
        get;
        set;
    }   

    void Awake()
    {
        if (tableVisual != null)
            tableVisual.ownerArea = this;
    }

    public void SetStatsFogged(bool fogged)
    {
        if (AreaATKText != null) AreaATKText.gameObject.SetActive(!fogged);
        if (AreaHealthText != null) AreaHealthText.gameObject.SetActive(!fogged);
    }   

    public void RefreshAreaStats()
    {
        if (AreaATKText != null)
        {
            if (TurnManager.Instance != null && TurnManager.Instance.IsBattlePhase)
            {
                Player localPlayer = GlobalSettings.Instance.localPlayer;
                AreaPosition localPos = localPlayer == GlobalSettings.Instance.LowPlayer
                    ? AreaPosition.Low : AreaPosition.Top;

                if (owner == localPos && parentZone != null)
                {
                    ZoneCombatResolver resolver = parentZone.GetComponent<ZoneCombatResolver>();
                    if (resolver != null)
                        AreaATKText.text = resolver.GetRemainingPool(owner).ToString();
                }
                else
                {
                    AreaATKText.text = GetTotalATK().ToString();
                }
            }
            else
            {
                AreaATKText.text = GetTotalATK().ToString();
            }
        }

        if (AreaHealthText != null)
            AreaHealthText.text = GetTotalHealth().ToString();
    }

    Player GetOwnerPlayer()
    {
        if (GlobalSettings.Instance == null) return null;
        return owner == AreaPosition.Low
            ? GlobalSettings.Instance.LowPlayer
            : GlobalSettings.Instance.TopPlayer;
    }

    int GetTotalATK()
    {
        int total = 0;
        foreach (GameObject creature in tableVisual.CreaturesOnTable)
        {
            OneCreatureManager ocm = creature.GetComponent<OneCreatureManager>();
            if (ocm != null && int.TryParse(ocm.AttackText.text, out int atk)) total += atk;
        }
        Player p = GetOwnerPlayer();
        if (p != null && parentZone != null)
            foreach (BuildingLogic bl in p.table.BuildingsInPlay)
                if (bl.Attack > 0 && bl.OriginSpot?.Zone == parentZone)
                    total += bl.Attack;
        return total;
    }

    int GetTotalHealth()
    {
        int total = 0;
        foreach (GameObject creature in tableVisual.CreaturesOnTable)
        {
            OneCreatureManager ocm = creature.GetComponent<OneCreatureManager>();
            if (ocm != null && int.TryParse(ocm.HealthText.text, out int hp)) total += hp;
        }
        Player p = GetOwnerPlayer();
        if (p != null && parentZone != null)
            foreach (BuildingLogic bl in p.table.BuildingsInPlay)
            {
                if (bl.OriginSpot?.Zone == parentZone && bl.Attack > 0)
                {   
                    total += bl.Health;
                }
            }
        return total;
    }



}
