using System.Collections.Generic;

[System.Serializable]
public struct EffectParameters
{
    public int Amount;
    public CardAsset TokenToSummon;
    public TokenPlacement Placement;
}

public enum TokenPlacement
{
    None,
    ToHand,
    ToZone,
}