using System.Collections.Generic;

[System.Serializable]
public class CardEffectData
{
    public EffectSO Effect;
    public TriggerType Trigger;
    public EffectObjectType TargetType;
    public List<TargetModifier> TargetModifiers;
    public TargetLocation TargetLocation; // pour les effets qui nécessitent une sélection de zone ou de joueur
    public EffectParameters Parameters;
    public ConditionSO Condition;
    public bool RequiresZoneInput => TargetLocation == TargetLocation.SelectedZone;
}