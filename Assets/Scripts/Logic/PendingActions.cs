using System.Collections.Generic;

public enum ActionType
{
    PlayCreature,
    MoveCreature,
    // We'll add Attack types later
}

public struct PendingAction
{
    public ActionType type;
    public int playerIndex;  // which player queued this action (0 = P1, 1 = P2)
    public int param1;       // PlayCreature: cardUniqueID    | MoveCreature: creatureUniqueID
    public int param2;       // PlayCreature: creatureUniqueID| MoveCreature: targetBaseID
    public int param3;       // PlayCreature: tablePos        | MoveCreature: tablePos
    public int param4;       // PlayCreature: baseID          | MoveCreature: (unused)
}
