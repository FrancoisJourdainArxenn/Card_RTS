public struct AttackHitResult
{
    public readonly int TargetUniqueID;
    public readonly int Damage;
    public readonly int HealthAfter;

    public AttackHitResult(int targetUniqueID, int damage, int healthAfter)
    {
        TargetUniqueID = targetUniqueID;
        Damage = damage;
        HealthAfter = healthAfter;
    }
}
