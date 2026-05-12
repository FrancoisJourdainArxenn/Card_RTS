using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CardEffectData
{
    public string EffectName;
    public EffectSO Effect;
    public TriggerType Trigger;
    public ConditionSO Condition;
    public EffectInfo Effectinfo;
    public EffectParameters Parameters;
    public EffectVisualData VisualData;

    public bool RequiresPlayerInput
    {
        get
        {
            foreach (EffectTargetInfo targetInfo in Effectinfo.effectTargets)
            {
                if (targetInfo.requiresPlayerSelection)
                    return true;
            }
            return false;
        }
    }
}
