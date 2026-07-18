public static class HeroCombatStatHooks
{
    public static void RecordDamageDealt(int attackerID, bool attackerIsBuilding, int damage)
    {
        if (damage <= 0) return;
        Player owner = attackerIsBuilding
            ? BuildingLogic.BuildingsCreatedThisGame.TryGetValue(attackerID, out BuildingLogic b) ? b.owner : null
            : CreatureLogic.CreaturesCreatedThisGame.TryGetValue(attackerID, out CreatureLogic c) ? c.owner : null;

        if (owner != null)
            owner.matchStats.Add(MatchStatType.DamageDealt, damage);
    }
}
