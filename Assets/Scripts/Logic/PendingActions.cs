using System.Collections.Generic;

public enum ActionType
{
    PlayCreature,
    MoveCreature,
    PlaceBuilding,
    PlaySpell,
    BoardCreature,
    // We'll add Attack types later
}

public struct PendingAction
{
    public ActionType type;
    public int playerIndex;  // which player queued this action (0 = P1, 1 = P2)
    public int param1;       // PlayCreature: cardUniqueID    | MoveCreature: creatureUniqueID | PlaySpell: cardUniqueID | BoardCreature: passengerUniqueID
    public int param2;       // PlayCreature: creatureUniqueID| MoveCreature: targetBaseID      | BoardCreature: transportUniqueID
    public int param3;       // PlayCreature: tablePos        | MoveCreature: tablePos          | BoardCreature: boardOrderPos (position gauche-à-droite ghost-free dans la rangée d'origine, voir GameNetworkManager.SortBoardActionsInPlace)
    public int param4;       // PlayCreature: baseID          | MoveCreature: (unused)
}
