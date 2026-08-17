public enum TriggerType
{
    // Passive - player dependant
    // Passive,
    // OnActivation,
    // Card Lifecycle
    OnPlay = 0,
    // OnAmbush,
    // OnTakeDamage,
    OnDeath = 1,
    OnAttack = 15, // déclenché au milieu de l'animation d'attaque, entre le wind-up et la charge/le projectile
    // Phases
    OnRegroup = 2,
    OnCommand = 3,
    OnBeginCombat = 4,
    OnBattleStart = 14, // déclenché par zone, au moment où CETTE zone commence son combat — pas à l'entrée de phase comme OnBeginCombat
    OnBattleEnd = 5,
    OnEndTurn = 6,
   
    // Other objects dying
    OnFriendlyCreatureDies = 7,
    OnEnemyCreatureDies = 8,
    OnFriendlyBuildingDies = 9,
    OnEnemyBuildingDies = 10,

    // Token
    OnTokenCreated = 11,
    
    //Reactions
    OnCardPlayed = 12,
    OnRessourceSpent = 13,
}