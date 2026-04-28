Ready for review
Select text to add comments on the plan
Plan : Système d'effets de cartes
Contexte
Le système actuel utilise des chaînes de caractères et de la réflexion (System.Activator.CreateInstance(Type.GetType("NomDeLaClasse"))) pour instancier les effets. Cela pose plusieurs problèmes :

Un seul effet par carte (un seul SpellScriptName / CreatureScriptName)
Un seul paramètre numérique (specialAmount)
Un nouveau fichier C# pour chaque carte même si les effets se ressemblent
Pas de sécurité : une faute de frappe dans la chaîne ne génère aucune erreur à la compilation
L'objectif est de remplacer ce système par des briques réutilisables (ScriptableObjects) combinables sans écrire de nouveau code pour chaque carte.

Réponse aux questions de design
ScriptableObjects ou CSV ?
Recommandation : ScriptableObjects maintenant, importer CSV plus tard si besoin.

Jusqu'à ~100 cartes : les .asset Unity sont parfaits. Drag-and-drop dans l'Inspector, pas de fautes de frappe, compatibles avec le versioning git.
À partir de 100-200 cartes : ajouter un outil d'import CSV optionnel qui lit les valeurs numériques (dégâts, coûts) depuis un Google Sheet et les écrit dans les assets existants. Les références structurelles (quel effet, quel trigger) restent dans l'Inspector.
Le système qu'on va construire est déjà conçu pour que cet import soit possible plus tard.
Comment structurer les effets ? (modèle Trigger → Condition → Effect)
C'est exactement la bonne structure. C'est aussi celle de Slay the Spire :

Trigger : quand l'effet se déclenche (au jeu de la carte, à la mort, au début du tour…)
Condition (optionnelle) : filtre qui doit être vrai pour que l'effet s'exécute
Effect : ce qui se passe concrètement (inflige X dégâts, pioche Y cartes…)
Targeting : sur qui l'effet s'applique
Architecture : les 3 couches
CardAsset (donnée)
  └── List<CardEffectData>        ← plusieurs effets par carte, chacun avec :
        ├── EffectSO              ← la brique réutilisable (ScriptableObject)
        ├── TriggerType           ← quand ça déclenche
        ├── TargetingType         ← sur qui
        └── EffectParameters      ← les nombres (Amount, SecondAmount, etc.)

EffectProcessor (static)          ← le hub central qui écoute les événements
                                    et déclenche les effets au bon moment
Nouveaux fichiers à créer
Tous dans Assets/Scripts/Logic/Effects/ :

1. TriggerType.cs — enum des déclencheurs
public enum TriggerType
{
    OnPlay,                    // Cri de bataille : quand la carte/créature est jouée
    OnDeath,                   // Rale d'agonie : quand cette créature meurt
    OnTurnStart,               // Début du tour du propriétaire
    OnTurnEnd,                 // Fin du tour du propriétaire
    OnAttack,                  // Cette créature attaque
    OnTakeDamage,              // Cette créature reçoit des dégâts
    OnAnyCreatureDies,         // N'importe quelle créature meurt
    OnFriendlyCreatureDies,    // Une créature alliée meurt
    OnEnemyCreatureDies,       // Une créature ennemie meurt
    OnAnyCreaturePlayed,       // N'importe quelle créature est jouée
    OnFriendlyCreaturePlayed,  // Une créature alliée est jouée
}
2. TargetingType.cs — enum des cibles
public enum TargetingType
{
    Self,                    // Le joueur qui a joué la carte
    Opponent,                // Le joueur adverse
    TargetCreature,          // La créature explicitement ciblée par le joueur
    SourceCreature,          // La créature qui possède cet effet (battlecry/deathrattle)
    AllFriendlyCreatures,
    AllEnemyCreatures,
    AllCreatures,
    AllFriendlyBuildings,
    AllEnemyBuildings,
    EventSubject,            // La créature qui vient de mourir/être jouée (pour les triggers réactifs)
}
3. EffectParameters.cs — le sac de paramètres
[System.Serializable]
public struct EffectParameters
{
    public int Amount;           // paramètre principal (dégâts, soins, nombre de pioches…)
    public int SecondAmount;     // paramètre secondaire (deuxième ressource, etc.)
    public bool UseSecondAmount;
    public CardAsset TokenToSummon; // pour invoquer un token
}
4. CardEffectData.cs — un slot d'effet sur une carte
[System.Serializable]
public class CardEffectData
{
    public EffectSO         Effect;     // la brique à exécuter
    public TriggerType      Trigger;    // quand
    public TargetingType    Targeting;  // sur qui
    public EffectParameters Parameters; // avec quels paramètres
    public ConditionSO      Condition;  // optionnel : filtre
}
5. EffectSO.cs — classe de base abstraite pour les briques
public abstract class EffectSO : ScriptableObject
{
    public abstract void Execute(EffectContext ctx, TargetingType targeting, EffectParameters parameters);
    public virtual string GetDescription(EffectParameters p) => "";
}
6. EffectContext.cs — contexte d'exécution passé à chaque effet
public class EffectContext
{
    public Player Caster;              // joueur qui a joué la carte
    public ILivable ExplicitTarget;    // cible choisie par le joueur (null si pas de cible)
    public CreatureLogic SourceCreature; // créature source de l'effet
    public BuildingLogic SourceBuilding;
    public CreatureLogic EventSubject; // pour les triggers : la créature qui vient de mourir/être jouée

    public Player Owner   => Caster;
    public Player Opponent => Caster?.otherPlayer;

    public List<ILivable> ResolveTargets(TargetingType targeting)
    {
        var list = new List<ILivable>();
        switch (targeting)
        {
            case TargetingType.Self:
                if (Caster != null) list.Add(Caster); break;
            case TargetingType.Opponent:
                if (Opponent != null) list.Add(Opponent); break;
            case TargetingType.TargetCreature:
                if (ExplicitTarget != null) list.Add(ExplicitTarget); break;
            case TargetingType.SourceCreature:
                if (SourceCreature != null) list.Add(SourceCreature); break;
            case TargetingType.AllEnemyCreatures:
                if (Opponent != null) list.AddRange(Opponent.table.CreaturesInPlay); break;
            case TargetingType.AllFriendlyCreatures:
                if (Caster != null) list.AddRange(Caster.table.CreaturesInPlay); break;
            case TargetingType.AllCreatures:
                foreach (Player p in Player.Players) list.AddRange(p.table.CreaturesInPlay); break;
            case TargetingType.AllFriendlyBuildings:
                if (Caster != null) list.AddRange(Caster.table.BuildingsInPlay); break;
            case TargetingType.AllEnemyBuildings:
                if (Opponent != null) list.AddRange(Opponent.table.BuildingsInPlay); break;
            case TargetingType.EventSubject:
                if (EventSubject != null) list.Add(EventSubject); break;
        }
        return list;
    }
}
7. ConditionSO.cs — base abstraite pour les conditions optionnelles
public abstract class ConditionSO : ScriptableObject
{
    public abstract bool Evaluate(EffectContext ctx);
}
8. EffectProcessor.cs — le hub central (static)
Ce fichier remplace toute la logique de RegisterEventEffect() / UnRegisterEventEffect() / WhenACreatureIsPlayed() / WhenACreatureDies().

public static class EffectProcessor
{
    private static Dictionary<TriggerType, List<RegisteredEffect>> _listeners
        = new Dictionary<TriggerType, List<RegisteredEffect>>();

    private struct RegisteredEffect
    {
        public CardEffectData Data;
        public System.Func<EffectContext> ContextFactory;
        public int OwnerID; // pour désenregistrer quand la créature meurt
    }

    // Appelé dans TurnManager.OnGameStart()
    public static void Reset() => _listeners.Clear();

    // Appelé dans le constructeur de CreatureLogic — enregistre les triggers non-OnPlay
    public static void RegisterCreatureEffects(CreatureLogic creature, CardAsset ca)
    {
        if (ca.Effects == null) return;
        foreach (var data in ca.Effects)
        {
            if (data.Trigger == TriggerType.OnPlay) continue;
            AddListener(data, creature.UniqueCreatureID, () => new EffectContext
            {
                Caster = creature.owner,
                SourceCreature = creature
            });
        }
    }

    // Appelé dans PlayACreatureFromHand / PlayASpellFromHand
    public static void FireOnPlay(CardAsset ca, EffectContext ctx)
    {
        if (ca.Effects == null) return;
        foreach (var data in ca.Effects)
        {
            if (data.Trigger != TriggerType.OnPlay) continue;
            TryExecute(data, ctx);
        }
    }

    // Appelé dans CreatureLogic.Die()
    public static void NotifyCreatureDied(CreatureLogic died, Player dyingOwner)
    {
        // Déclenche les OnDeath de la créature qui meurt
        if (died.ca.Effects != null)
        {
            foreach (var data in died.ca.Effects)
            {
                if (data.Trigger != TriggerType.OnDeath) continue;
                TryExecute(data, new EffectContext { Caster = dyingOwner, SourceCreature = died });
            }
        }
        // Notifie les autres créatures qui écoutent OnAnyCreatureDies etc.
        var eventCtx = new EffectContext { EventSubject = died };
        NotifyFiltered(TriggerType.OnAnyCreatureDies, eventCtx, _ => true);
        NotifyFiltered(TriggerType.OnFriendlyCreatureDies, eventCtx,
            re => re.ContextFactory().Caster == dyingOwner);
        NotifyFiltered(TriggerType.OnEnemyCreatureDies, eventCtx,
            re => re.ContextFactory().Caster != dyingOwner);
        UnregisterEntity(died.UniqueCreatureID);
    }

    // Appelé dans Player.OnTurnStart()
    public static void NotifyTurnStart(Player owner)
        => NotifyFiltered(TriggerType.OnTurnStart,
            new EffectContext { Caster = owner },
            re => re.ContextFactory().Caster == owner);

    // Appelé dans Player.OnTurnEnd()
    public static void NotifyTurnEnd(Player owner)
        => NotifyFiltered(TriggerType.OnTurnEnd,
            new EffectContext { Caster = owner },
            re => re.ContextFactory().Caster == owner);

    // Désenregistre tous les effets d'une entité (mort, destruction)
    public static void UnregisterEntity(int ownerID)
    {
        foreach (var list in _listeners.Values)
            list.RemoveAll(re => re.OwnerID == ownerID);
    }

    // ---- Méthodes privées ----
    private static void AddListener(CardEffectData data, int ownerID, System.Func<EffectContext> ctxFactory)
    {
        if (!_listeners.ContainsKey(data.Trigger))
            _listeners[data.Trigger] = new List<RegisteredEffect>();
        _listeners[data.Trigger].Add(new RegisteredEffect { Data = data, ContextFactory = ctxFactory, OwnerID = ownerID });
    }

    private static void NotifyFiltered(TriggerType trigger, EffectContext baseCtx, System.Func<RegisteredEffect, bool> filter)
    {
        if (!_listeners.TryGetValue(trigger, out var list)) return;
        var snapshot = new List<RegisteredEffect>(list);
        foreach (var re in snapshot)
        {
            if (!filter(re)) continue;
            EffectContext ctx = re.ContextFactory();
            ctx.EventSubject = baseCtx.EventSubject ?? ctx.EventSubject;
            TryExecute(re.Data, ctx);
        }
    }

    private static void TryExecute(CardEffectData data, EffectContext ctx)
    {
        if (data.Effect == null) return;
        if (data.Condition != null && !data.Condition.Evaluate(ctx)) return;
        data.Effect.Execute(ctx, data.Targeting, data.Parameters);
    }
}
Briques concrètes à créer (dans ConcreteEffects/)
DealDamageSO.cs
[CreateAssetMenu(menuName = "Effects/DealDamage")]
public class DealDamageSO : EffectSO
{
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        foreach (ILivable target in ctx.ResolveTargets(targeting))
        {
            new DealDamageCommand(target.ID, p.Amount, target.Health - p.Amount).AddToQueue();
            target.Health -= p.Amount;
        }
    }
    public override string GetDescription(EffectParameters p) => $"Inflige {p.Amount} dégâts";
}
HealSO.cs
[CreateAssetMenu(menuName = "Effects/Heal")]
public class HealSO : EffectSO
{
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        foreach (ILivable target in ctx.ResolveTargets(targeting))
            target.Health += p.Amount;
    }
}
DrawCardsSO.cs
[CreateAssetMenu(menuName = "Effects/DrawCards")]
public class DrawCardsSO : EffectSO
{
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        Player target = targeting == TargetingType.Opponent ? ctx.Opponent : ctx.Caster;
        if (target == null) return;
        for (int i = 0; i < p.Amount; i++)
            target.DrawACard();
        // Note : en mode réseau, utiliser GameNetworkManager.Instance.BroadCastDrawCard()
    }
}
GiveResourcesSO.cs
[CreateAssetMenu(menuName = "Effects/GiveResources")]
public class GiveResourcesSO : EffectSO
{
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        Player target = targeting == TargetingType.Opponent ? ctx.Opponent : ctx.Caster;
        target?.GetBonusRessources(p.Amount, p.UseSecondAmount ? p.SecondAmount : 0);
    }
}
GiveBuffSO.cs
[CreateAssetMenu(menuName = "Effects/GiveBuff")]
public class GiveBuffSO : EffectSO
{
    // p.Amount = bonus d'attaque, p.SecondAmount = bonus de vie
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        foreach (ILivable t in ctx.ResolveTargets(targeting))
            if (t is CreatureLogic creature)
                creature.ApplyBuff(p.Amount, p.SecondAmount);
    }
}
SummonTokenSO.cs
[CreateAssetMenu(menuName = "Effects/SummonToken")]
public class SummonTokenSO : EffectSO
{
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        if (p.TokenToSummon == null || ctx.Caster == null) return;
        for (int i = 0; i < p.Amount; i++)
            ctx.Caster.GetACardNotFromDeck(p.TokenToSummon);
    }
}
Modifications des fichiers existants
CardAsset.cs
Ajouter à la fin (garder tous les anciens champs pour la migration) :

[Header("Effects (nouveau système)")]
public List<CardEffectData> Effects = new List<CardEffectData>();
CreatureLogic.cs — constructeur
Après le bloc de réflexion existant (lignes 137-141) :

// Nouveau système
if (ca.Effects != null && ca.Effects.Count > 0)
    EffectProcessor.RegisterCreatureEffects(this, ca);
Ajouter la méthode ApplyBuff (nécessaire pour GiveBuffSO) :

public void ApplyBuff(int attackDelta, int healthDelta)
{
    baseAttack += attackDelta;
    baseHealth += healthDelta;
    if (healthDelta > 0) health += healthDelta;
}
CreatureLogic.Die() (ligne 153)
Après le bloc if (effect != null) existant :

// Nouveau système
EffectProcessor.NotifyCreatureDied(this, owner);
Player.PlayACreatureFromHand() (ligne 333)
Après if (newCreature.effect != null) newCreature.effect.WhenACreatureIsPlayed(); (ligne 344) :

// Nouveau système
EffectProcessor.FireOnPlay(playedCard.ca, new EffectContext
{
    Caster = this,
    ExplicitTarget = null, // pour une créature sans cible explicite
    SourceCreature = newCreature
});
Idem dans NetworkFlushPlayCreature() (ligne 397) et NetworkPlayCreatureFromHand() (ligne 428).

Player.PlayASpellFromHand() (ligne 306)
Après playedCard.effect.ActivateEffect(...) (ligne 312) :

// Nouveau système
EffectProcessor.FireOnPlay(playedCard.ca, new EffectContext
{
    Caster = this,
    ExplicitTarget = target
});
Player.OnTurnStart() (ligne 165)
Au début de la méthode :

EffectProcessor.NotifyTurnStart(this);
Player.OnTurnEnd() (ligne 205)
Au début de la méthode :

EffectProcessor.NotifyTurnEnd(this);
TurnManager.OnGameStart() (ligne 47)
Avec les autres .Clear() (ligne 77-79) :

EffectProcessor.Reset();
Exemple concret : "Ghost Marine – Cri de bataille : Inflige 2 dégâts à toutes les créatures ennemies"
Ancien système
Créer un nouveau fichier DamageAllEnemyCreaturesBattlecry.cs
Hériter de CreatureEffect, surcharger WhenACreatureIsPlayed()
Dans le CardAsset Inspector : CreatureScriptName = "DamageAllEnemyCreaturesBattlecry", specialCreatureAmount = 2
Nouveau système
Aucun nouveau fichier C#. Dans l'Inspector du CardAsset Ghost Marine :

Effects (size: 1)
  [0]
    Effect:     <glisser DealDamage.asset ici>
    Trigger:    OnPlay
    Targeting:  AllEnemyCreatures
    Parameters:
      Amount:   2
Pour ajouter "et aussi pioche 1 carte", ajouter un deuxième slot :

  [1]
    Effect:     <glisser DrawCards.asset ici>
    Trigger:    OnPlay
    Targeting:  Self
    Parameters:
      Amount:   1
Migration des cartes existantes
Table de correspondance pour les effets déjà en place :

Ancienne classe	Nouvelle EffectSO	Targeting	Amount
DealDamageToTarget	DealDamageSO	TargetCreature	specialSpellAmount
GiveRessourcesBonus	GiveResourcesSO	Self	specialSpellAmount
DamageOpponentBattlecry	DealDamageSO	Opponent	specialCreatureAmount
Plan de migration en 3 phases
Phase 1 (fondation, non-bloquante)
Créer tous les nouveaux fichiers
Ajouter List<CardEffectData> Effects dans CardAsset
Brancher les appels dans Player, CreatureLogic, TurnManager
Les 26 cartes existantes ont Effects vide → elles continuent de fonctionner via l'ancien système
Phase 2 (migration des cartes existantes)
Pour chaque carte existante : configurer les Effects dans l'Inspector, vider les anciens champs ScriptName
Phase 3 (nettoyage, plus tard)
Supprimer SpellEffect.cs, CreatureEffect.cs, tous les fichiers dans SpellScripts/ et CreatureScripts/
Supprimer CreatureScriptName, SpellScriptName, BuildingScriptName, specialCreatureAmount/SpellAmount/BuildingAmount de CardAsset
Supprimer les anciens chemins de code dans Player.cs, CreatureLogic.cs
Vérification
Après implémentation, tester :

Lancer une partie locale — les cartes existantes fonctionnent encore (ancien système)
Créer un CardAsset test avec DealDamageSO (OnPlay, AllEnemyCreatures, Amount=2) — jouer la carte inflige bien 2 dégâts à toutes les créatures ennemies
Tester un trigger réactif : créer une carte avec DealDamageSO (OnDeath, Opponent, Amount=1) — quand la créature meurt, le joueur adverse perd 1 point de vie
Tester OnTurnStart : créer une carte avec GiveResourcesSO (OnTurnStart, Self, Amount=1) — le joueur gagne 1 ressource par tour tant que la créature est en jeu
Tester en multijoueur — vérifier que les effets se déclenchent des deux côtés de façon identique
Add Comment