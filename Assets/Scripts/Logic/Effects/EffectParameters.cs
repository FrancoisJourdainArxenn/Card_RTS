using System.Collections.Generic;

public struct EffectParameters
{
    public int Amount;
    public CardAsset TokenToSummon;
    public TokenPlacement Placement;
}

public enum TokenPlacement
{
    ToHand,
    ToZone,
}