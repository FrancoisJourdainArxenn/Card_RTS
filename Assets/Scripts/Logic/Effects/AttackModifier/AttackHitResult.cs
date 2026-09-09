public struct AttackHitResult
{
    public readonly int TargetUniqueID;
    public readonly int Damage;
    public readonly int HealthAfter;
    // Bouclier de la cible absorbé par CE coup précisément, précalculé pendant la planification
    // (ZoneCombatResolver.AddPendingCreatureDamage) en utilisant le bouclier RESTANT au moment de ce
    // coup — jamais recalculé plus tard à partir de ShieldValue "final" (voir EnqueueBattleCommands),
    // pour ne pas créditer rétroactivement un bouclier gagné APRÈS ce coup.
    public readonly int Absorbed;

    public AttackHitResult(int targetUniqueID, int damage, int healthAfter, int absorbed = 0)
    {
        TargetUniqueID = targetUniqueID;
        Damage = damage;
        HealthAfter = healthAfter;
        Absorbed = absorbed;
    }
}
