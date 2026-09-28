public enum TriggerType
{
    // Passive - player dependant
    // OnActivation,
    // Card Lifecycle
    OnPlay = 0,
    // OnAmbush,
    OnTakeDamage = 16, // déclenché à chaque fois que l'unité subit des dégâts, quel que soit le montant,
                        // même si ce coup la tue — une fois par coup, au moment où CE coup précis est
                        // révélé visuellement (voir ZoneCombatResolver.OnTakeDamageDeferKey et
                        // CreatureLogic.ResolveOnTakeDamageFromEffect)
    OnAllyTakeDamage = 18, // déclenché chez les AUTRES créatures alliées quand l'une d'elles subit des
                            // dégâts (voir EffectRegistry.NotifyCreatureTookDamage/-Predicted)
    OnDeath = 1,
    OnAttack = 15, // déclenché au milieu de l'animation d'attaque, entre le wind-up et la charge/le projectile
    OnFriendlyUnitAttacks = 20, // déclenché chez les AUTRES créatures alliées quand l'une d'elles attaque —
                                // une fois par attaque, au même moment que le OnAttack de l'attaquant (voir
                                // EffectRegistry.NotifyFriendlyUnitAttackedPredicted / CreatureLogic.ResolvePredictedOnAttack)
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
    // 9/10 = anciens OnFriendly/EnemyBuildingDies (retirés) — ne pas réutiliser ces valeurs.

    // Token
    OnTokenCreated = 11,
    
    //Reactions
    OnCardPlayed = 12,
    OnRessourceSpent = 13,
    OnEachRessourceSpent = 22, // déclenché une fois PAR Ressource dépensée (une dépense de 5 = 5 déclenchements),
                               // contrairement à OnRessourceSpent (une fois par dépense, quel que soit le montant).
                               // Combiné à un CondCounter WrappedCondition + resetCount → "toutes les N Ressources".
    OnActionPlayed = 17, // déclenché quand une carte Action (sort/order) est jouée/lancée, via ETB
                         // (main du joueur ou CastSpellSO) — voir EffectRegistry.NotifyActionPlayed
    OnTierUpgrade = 19, // déclenché une seule fois, quand BaseLogic.TryUpgrade() fait passer
                        // CurrentTier à une nouvelle valeur — voir EffectRegistry.NotifyBaseTierUpgraded

    Passive = 21, // actif en continu tant que la source (créature/base) est en jeu, réévalué
                  // (Condition + ciblage) à chaque changement de plateau — voir PassiveAuraManager.
}