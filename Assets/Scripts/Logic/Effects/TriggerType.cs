public enum TriggerType
{
    // Passive - player dependant
    // Passive,
    // OnActivation,
    // Card Lifecycle
    OnPlay,
    // OnAmbush,
    // OnAttack,
    // OnTakeDamage,
    OnDeath,
    // Phases
    OnRegroup,
    OnCommand,
    OnBeginCombat,
    OnBattleEnd,
    OnEndTurn,
    // Other objects dying
    OnFriendlyCreatureDies,
    OnEnemyCreatureDies,
    OnFriendlyBuildingDies,
    OnEnemyBuildingDies,

    // Token
    OnTokenCreated,
    //Reactions
    OnCardPlayed,
}