using UnityEngine;
using System.Collections;
using TMPro;

public enum AreaPosition{Top, Low} // Interesting

public class PlayerArea : MonoBehaviour 
{
    public AreaPosition owner;
    public bool ControlsON = true;

    public HandVisual handVisual;
    //public BaseVisual Portrait;

    //public EndTurnButton EndTurnButton;
    public TableVisual tableVisual;
    public Transform BasePosition;

    public bool AllowedToControlThisPlayer
    {
        get;
        set;
    }      


}
