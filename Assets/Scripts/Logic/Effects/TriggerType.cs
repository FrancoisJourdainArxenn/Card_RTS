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
    OnRessourceSpent,
    // Zone (déclenché par zone, au moment où CETTE zone commence son combat — pas à l'entrée
    // de phase comme OnBeginCombat). Ajouté en dernier pour ne pas décaler les valeurs des
    // triggers existants déjà sérialisés sur les cartes.
    OnBattleStart,
}