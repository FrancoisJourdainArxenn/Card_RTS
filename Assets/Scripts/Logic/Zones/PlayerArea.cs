using UnityEngine;
using System.Collections;
using TMPro;

public enum AreaPosition{Top, Low, Neutral} // Interesting

public class PlayerArea : MonoBehaviour 
{
    public AreaPosition owner;
    public bool ControlsON = true;
    public TableVisual tableVisual;
    public Transform BasePosition;
    public int baseID;

    [HideInInspector]
    public ZoneManager parentZone;
    public Transform BattlePos;

    public TMP_Text meleeCountText;
    public TMP_Text rangedCountText;

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
    void Update()
    {
        if (tableVisual == null || GlobalSettings.Instance == null) return;

        int maxPerRow = GlobalSettings.Instance.MaxCreaturePerRow;

        if (meleeCountText != null)
            meleeCountText.text = $"{tableVisual.EffectiveRowCount(true)}/{maxPerRow} M";

        if (rangedCountText != null)
            rangedCountText.text = $"{tableVisual.EffectiveRowCount(false)}/{maxPerRow} R";
    }

    public Player GetOwnerPlayer()
    {
        if (GlobalSettings.Instance == null) return null;
        return owner == AreaPosition.Low
            ? GlobalSettings.Instance.LowPlayer
            : GlobalSettings.Instance.TopPlayer;
    }

}
