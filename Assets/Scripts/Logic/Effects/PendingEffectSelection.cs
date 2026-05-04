using System.Collections.Generic;

public class PendingEffectSelection
{
    public CardEffectData Data;
    public EffectContext Context;
    public List<IIdentifiable> EligibleTargets;
    public IIdentifiable SelectedTarget;
    public int SourceEntityID;   // unique ID of the creature/building owning this effect
    public int EffectIndexInCard; // index in CardAsset.Effects[]
}
