What's done
Logic layer — BuildingLogic.cs is complete. A building can:

Track health/max health, attack, attacks per turn
Die (removes itself from Table.BuildingsInPlay, queues BuildingDieCommand)
Attack a creature, a base, or another building (queues BuildingAttackCommand each time)
Be looked up via BuildingLogic.BuildingsCreatedThisGame[id]
Commands — two new commands are ready:

BuildingDieCommand.cs: destroys the GameObject, notifies the OriginSpot so the build spot reopens
BuildingAttackCommand.cs: shows damage effects and updates health text on attacker and target (works for creature, base, or building targets)
BaseDieCommand.cs: now in its own clean file
What still needs to be connected
Table.cs — needs public List<BuildingLogic> BuildingsInPlay added so buildings are tracked per player
PlaceBuildingCommand.cs — needs to instantiate a BuildingLogic and assign it to the OneBuildingManager
OneBuildingManager.cs — needs a BuildingLogic reference, and its TakeDamage method should delegate to BuildingLogic.Health -= amount instead of handling death itself
TurnManager (or wherever turn-start happens) — needs to call OnTurnStart() on each building so AttacksLeftThisTurn resets
Targeting/combat UI — nothing wires up "player clicks building, clicks enemy" yet
Want to tackle those in order, starting with Table.cs and PlaceBuildingCommand?