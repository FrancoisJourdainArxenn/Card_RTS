using System.Collections.Generic;

[System.Serializable]
public class CardEffectData
{
    public string EffectName;
    public EffectSO Effect;
    public TriggerType Trigger;
    public ConditionSO Condition;
    public EffectInfo Effectinfo;
    public EffectParameters Parameters;
        public bool RequiresZoneInput {
        get
        {
            foreach (TargetInfo targetInfo in Effectinfo.effectTargets)
            {
                if (targetInfo.targetType == EffectObjectType.Zone)
                    return true;
            }
            return false;
        }
    }
}