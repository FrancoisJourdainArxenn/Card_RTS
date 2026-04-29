# Plan : Système d'effets de cartes

## Contexte

Le système actuel utilise des chaînes de caractères et de la réflexion (`System.Activator.CreateInstance(Type.GetType("NomDeLaClasse"))`) pour instancier les effets. Cela pose plusieurs problèmes :
- **Un seul effet par carte** (un seul `SpellScriptName` / `CreatureScriptName`)
- **Un seul paramètre numérique** (`specialAmount`)
- **Un nouveau fichier C# pour chaque carte** même si les effets se ressemblent
- **Pas de sécurité** : une faute de frappe dans la chaîne ne génère aucune erreur à la compilation

L'objectif est de remplacer ce système par des **briques réutilisables** (ScriptableObjects) combinables sans écrire de nouveau code pour chaque carte.

---

## Réponse aux questions de design

### ScriptableObjects ou CSV ?
**Recommandation : ScriptableObjects maintenant, importer CSV plus tard si besoin.**

- Jusqu'à ~100 cartes : les `.asset` Unity sont parfaits. Drag-and-drop dans l'Inspector, pas de fautes de frappe, compatibles avec le versioning git.
- À partir de 100-200 cartes : ajouter un outil d'import CSV **optionnel** qui lit les valeurs numériques (dégâts, coûts) depuis un Google Sheet et les écrit dans les assets existants. Les références structurelles (quel effet, quel trigger) restent dans l'Inspector.
- Le système qu'on va construire est déjà conçu pour que cet import soit possible plus tard.

### Comment structurer les effets ? (modèle Trigger → Condition → Effect)
C'est exactement la bonne structure. C'est aussi celle de Slay the Spire :
- **Trigger** : quand l'effet se déclenche (au jeu de la carte, à la mort, au début du tour…)
- **Condition** (optionnelle) : filtre qui doit être vrai pour que l'effet s'exécute
- **Effect** : ce qui se passe concrètement (inflige X dégâts, pioche Y cartes…)
- **Targeting** : sur qui l'effet s'applique

---

## Architecture : les 3 couches

```
CardAsset (donnée)
  └── List<CardEffectData>        ← plusieurs effets par carte, chacun avec :
        ├── EffectSO              ← la brique réutilisable (ScriptableObject)
        ├── TriggerType           ← quand ça déclenche
        ├── TargetingType         ← sur qui
        └── EffectParameters      ← les nombres (Amount, SecondAmount, etc.)

EffectProcessor (static)          ← le hub central qui écoute les événements
                                    et déclenche les effets au bon moment
```

---

## Structure des dossiers

```
Assets/Scripts/Logic/
├── Effects/                        ← nouveau dossier principal
│   ├── TriggerType.cs
│   ├── TargetingType.cs
│   ├── TokenPlacement.cs
│   ├── EffectParameters.cs
│   ├── CardEffectData.cs
│   ├── EffectSO.cs
│   ├── EffectContext.cs
│   ├── EffectProcessor.cs
│   ├── ConditionSO.cs
│   ├── ConcreteEffects/            ← les briques réutilisables
│   │   ├── DealDamageSO.cs
│   │   ├── HealSO.cs
│   │   ├── DrawCardsSO.cs
│   │   ├── GiveResourcesSO.cs
│   │   ├── GiveBuffSO.cs
│   │   ├── SummonTokenSO.cs
│   │   └── StaticAuraSO.cs
│   └── ConcreteConditions/         ← les filtres optionnels
│       ├── HasNOrMoreCreaturesCondition.cs
│       └── MinTurnCondition.cs
├── SpellScripts/                   ← ANCIEN système (à supprimer en Phase 3)
└── CreatureScripts/                ← ANCIEN système (à supprimer en Phase 3)
```

Les assets Unity (`.asset`) correspondant aux briques vont dans :
```
Assets/Data_Base/Effects/
├── DealDamage.asset
├── Heal.asset
├── DrawCards.asset
├── GiveResources.asset
├── GiveBuff.asset
└── SummonToken.asset
```

---

## Nouveaux fichiers à créer

Tous dans `Assets/Scripts/Logic/Effects/` :

### 1. `TriggerType.cs` — enum des déclencheurs
```csharp
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
    Aura,                      // Effet continu : actif tant que la créature source est en vie
}
```

### 2. `TargetingType.cs` — enum des cibles
```csharp
public enum TargetingType
{
    Self,                          // Le joueur qui a joué la carte
    Opponent,                      // Le joueur adverse
    TargetCreature,                // La créature explicitement ciblée par le joueur
    TargetBuilding,                // Le bâtiment explicitement ciblé par le joueur
    SourceCreature,                // La créature qui possède cet effet (battlecry/deathrattle)
    AllFriendlyCreatures,
    AllEnemyCreatures,
    AllCreatures,
    AllFriendlyBuildings,
    AllEnemyBuildings,
    EventSubject,                  // La créature qui vient de mourir/être jouée (triggers réactifs)

    // Ciblage par zone (baseID = zone/PlayerArea)
    // La zone de référence est celle de SourceCreature ou SourceBuilding
    AllCreaturesInZone,            // Toutes créatures dans la même zone que la source
    AllFriendlyCreaturesInZone,    // Créatures alliées dans la même zone
    AllEnemyCreaturesInZone,       // Créatures ennemies dans la même zone
    AllBuildingsInZone,            // Tous bâtiments dans la même zone
    AllFriendlyBuildingsInZone,    // Bâtiments alliés dans la même zone
    AllEnemyBuildingsInZone,       // Bâtiments ennemis dans la même zone
}
```

> **Comment fonctionne le ciblage par zone ?**
> Chaque créature a un `BaseID` qui correspond à une `PlayerArea`. "Dans la même zone" = même `BaseID` que `SourceCreature.BaseID`. `EffectContext.ResolveTargets()` filtre les listes `CreaturesInPlay` et `BuildingsInPlay` avec cette condition.

### 3. `EffectParameters.cs` — le sac de paramètres
```csharp
[System.Serializable]
public struct EffectParameters
{
    public int Amount;                // paramètre principal (dégâts, soins, nombre de pioches…)
    public int SecondAmount;          // paramètre secondaire (deuxième ressource, etc.)
    public bool UseSecondAmount;
    public CardAsset TokenToSummon;   // pour invoquer un token
    public TokenPlacement Placement;  // où le token apparaît (voir enum ci-dessous)
}

public enum TokenPlacement
{
    ToHand,         // Le token arrive dans la main du joueur (joué manuellement ensuite)
    ToSourceZone,   // Le token apparaît directement dans la zone de la créature source
    ToMainZone,     // Le token apparaît directement dans la zone principale du joueur
}
```

> **Comment ça marche concrètement ?**
> - `ToHand` → appelle `caster.GetACardNotFromDeck(token)` — le token est une carte dans la main
> - `ToSourceZone` / `ToMainZone` → appelle directement `caster.PlayACreatureFromHand(...)` avec le bon `PlayerArea`, sans déduire de ressources (c'est un token gratuit)
> Le `SummonTokenSO` lit `p.Placement` pour décider quel chemin emprunter.

### 4. `CardEffectData.cs` — un slot d'effet sur une carte
```csharp
[System.Serializable]
public class CardEffectData
{
    public EffectSO         Effect;     // la brique à exécuter
    public TriggerType      Trigger;    // quand
    public TargetingType    Targeting;  // sur qui
    public EffectParameters Parameters; // avec quels paramètres
    public ConditionSO      Condition;  // optionnel : filtre
}
```

### 5. `EffectSO.cs` — classe de base abstraite pour les briques
```csharp
public abstract class EffectSO : ScriptableObject
{
    public abstract void Execute(EffectContext ctx, TargetingType targeting, EffectParameters parameters);
    public virtual string GetDescription(EffectParameters p) => "";
}
```

### 6. `EffectContext.cs` — contexte d'exécution passé à chaque effet
```csharp
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
        // L'ID de zone de référence (pour les ciblages InZone)
        int zoneID = SourceCreature?.BaseID ?? SourceBuilding?.BaseBuildingID ?? -1;

        switch (targeting)
        {
            case TargetingType.Self:
                if (Caster != null) list.Add(Caster); break;
            case TargetingType.Opponent:
                if (Opponent != null) list.Add(Opponent); break;
            case TargetingType.TargetCreature:
                if (ExplicitTarget is CreatureLogic) list.Add(ExplicitTarget); break;
            case TargetingType.TargetBuilding:
                if (ExplicitTarget is BuildingLogic) list.Add(ExplicitTarget); break;
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

            // --- Ciblages par zone ---
            // "Zone" = toutes les créatures/bâtiments dont le BaseID correspond à la zone source
            case TargetingType.AllCreaturesInZone:
                foreach (Player p in Player.Players)
                    list.AddRange(p.table.CreaturesInPlay.FindAll(c => c.BaseID == zoneID));
                break;
            case TargetingType.AllFriendlyCreaturesInZone:
                if (Caster != null)
                    list.AddRange(Caster.table.CreaturesInPlay.FindAll(c => c.BaseID == zoneID));
                break;
            case TargetingType.AllEnemyCreaturesInZone:
                if (Opponent != null)
                    list.AddRange(Opponent.table.CreaturesInPlay.FindAll(c => c.BaseID == zoneID));
                break;
            case TargetingType.AllBuildingsInZone:
                foreach (Player p in Player.Players)
                    list.AddRange(p.table.BuildingsInPlay.FindAll(b => b.BaseBuildingID == zoneID));
                break;
            case TargetingType.AllFriendlyBuildingsInZone:
                if (Caster != null)
                    list.AddRange(Caster.table.BuildingsInPlay.FindAll(b => b.BaseBuildingID == zoneID));
                break;
            case TargetingType.AllEnemyBuildingsInZone:
                if (Opponent != null)
                    list.AddRange(Opponent.table.BuildingsInPlay.FindAll(b => b.BaseBuildingID == zoneID));
                break;
        }
        return list;
    }
}
```

### 7. `ConditionSO.cs` — base abstraite pour les conditions optionnelles
```csharp
public abstract class ConditionSO : ScriptableObject
{
    public abstract bool Evaluate(EffectContext ctx);
}
```

### 8. `EffectProcessor.cs` — le hub central (static)

Ce fichier remplace toute la logique de `RegisterEventEffect()` / `UnRegisterEventEffect()` / `WhenACreatureIsPlayed()` / `WhenACreatureDies()`.

```csharp
public static class EffectProcessor
{
    private static Dictionary<TriggerType, List<RegisteredEffect>> _listeners
        = new Dictionary<TriggerType, List<RegisteredEffect>>();

    // Auras actives en jeu — chacune mémorise qui elle a buffé
    private static List<AuraInstance> _activeAuras = new List<AuraInstance>();

    private class AuraInstance
    {
        public int SourceCreatureID;        // ID de la créature source de l'aura
        public StaticAuraSO Effect;         // l'effet à appliquer/annuler
        public TargetingType Targeting;     // qui est ciblé
        public EffectParameters Parameters; // les valeurs du buff
        public Player Owner;
        public List<int> BuffedIDs = new List<int>(); // IDs des créatures actuellement buffées
    }

    private struct RegisteredEffect
    {
        public CardEffectData Data;
        public System.Func<EffectContext> ContextFactory;
        public int OwnerID; // pour désenregistrer quand la créature meurt
    }

    // Appelé dans TurnManager.OnGameStart()
    public static void Reset()
    {
        _listeners.Clear();
        _activeAuras.Clear();
    }

    // Appelé dans le constructeur de CreatureLogic — enregistre les triggers non-OnPlay
    public static void RegisterCreatureEffects(CreatureLogic creature, CardAsset ca)
    {
        if (ca.Effects == null) return;
        foreach (var data in ca.Effects)
        {
            if (data.Trigger == TriggerType.OnPlay) continue;

            if (data.Trigger == TriggerType.Aura && data.Effect is StaticAuraSO auraSO)
            {
                // Créer l'instance d'aura et l'appliquer immédiatement aux cibles actuelles
                var aura = new AuraInstance
                {
                    SourceCreatureID = creature.UniqueCreatureID,
                    Effect           = auraSO,
                    Targeting        = data.Targeting,
                    Parameters       = data.Parameters,
                    Owner            = creature.owner
                };
                ApplyAuraToCurrentTargets(aura, creature);
                _activeAuras.Add(aura);
            }
            else
            {
                AddListener(data, creature.UniqueCreatureID, () => new EffectContext
                {
                    Caster = creature.owner,
                    SourceCreature = creature
                });
            }
        }
    }

    // Appelé depuis Player.PlayACreatureFromHand — met à jour les auras existantes
    public static void NotifyCreaturePlayed(CreatureLogic played)
    {
        foreach (AuraInstance aura in _activeAuras)
        {
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(aura.SourceCreatureID, out var source)) continue;
            EffectContext ctx = new EffectContext { Caster = aura.Owner, SourceCreature = source };
            var targets = ctx.ResolveTargets(aura.Targeting);
            // Si la nouvelle créature fait partie des cibles et n'est pas déjà buffée
            if (targets.Contains(played) && !aura.BuffedIDs.Contains(played.UniqueCreatureID))
            {
                aura.Effect.Apply(played, aura.Parameters);
                aura.BuffedIDs.Add(played.UniqueCreatureID);
            }
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
        // Retire les auras dont cette créature était la source
        var aurasByDead = _activeAuras.FindAll(a => a.SourceCreatureID == died.UniqueCreatureID);
        foreach (AuraInstance aura in aurasByDead)
            RemoveAura(aura);
        _activeAuras.RemoveAll(a => a.SourceCreatureID == died.UniqueCreatureID);

        // Retire cette créature des listes de buff des auras actives (elle est morte, inutile d'annuler)
        foreach (AuraInstance aura in _activeAuras)
            aura.BuffedIDs.Remove(died.UniqueCreatureID);

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

    // Applique une aura à toutes ses cibles actuelles (appelé à l'entrée en jeu de la source)
    private static void ApplyAuraToCurrentTargets(AuraInstance aura, CreatureLogic source)
    {
        EffectContext ctx = new EffectContext { Caster = aura.Owner, SourceCreature = source };
        foreach (ILivable t in ctx.ResolveTargets(aura.Targeting))
        {
            if (t is CreatureLogic creature && !aura.BuffedIDs.Contains(creature.UniqueCreatureID))
            {
                aura.Effect.Apply(creature, aura.Parameters);
                aura.BuffedIDs.Add(creature.UniqueCreatureID);
            }
        }
    }

    // Annule tous les buffs d'une aura (appelé quand la source meurt)
    private static void RemoveAura(AuraInstance aura)
    {
        foreach (int id in aura.BuffedIDs)
        {
            if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic creature))
                aura.Effect.Remove(creature, aura.Parameters);
        }
        aura.BuffedIDs.Clear();
    }
}
```

---

## Briques concrètes à créer (dans `ConcreteEffects/`)

### `DealDamageSO.cs`
```csharp
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
```

### `HealSO.cs`
```csharp
[CreateAssetMenu(menuName = "Effects/Heal")]
public class HealSO : EffectSO
{
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        foreach (ILivable target in ctx.ResolveTargets(targeting))
            target.Health += p.Amount;
    }
}
```

### `DrawCardsSO.cs`
```csharp
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
```

### `GiveResourcesSO.cs`
```csharp
[CreateAssetMenu(menuName = "Effects/GiveResources")]
public class GiveResourcesSO : EffectSO
{
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        Player target = targeting == TargetingType.Opponent ? ctx.Opponent : ctx.Caster;
        target?.GetBonusRessources(p.Amount, p.UseSecondAmount ? p.SecondAmount : 0);
    }
}
```

### `GiveBuffSO.cs`
```csharp
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
```

### `StaticAuraSO.cs`

Une aura statique applique un buff à ses cibles **tant que la créature source est en vie**. Quand la source meurt, le buff est annulé sur toutes les créatures qu'elle buffait.

Contrairement aux autres `EffectSO`, `StaticAuraSO` ne passe pas par `Execute()`. Elle est gérée directement par `EffectProcessor` via des `AuraInstance`.

```csharp
[CreateAssetMenu(menuName = "Effects/StaticAura")]
public class StaticAuraSO : EffectSO
{
    // Pas utilisé directement — l'EffectProcessor gère les auras via AuraInstance
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p) { }

    // Applique le buff à une créature
    public void Apply(CreatureLogic target, EffectParameters p)
    {
        target.ApplyBuff(p.Amount, p.SecondAmount);
    }

    // Annule le buff sur une créature (valeurs inverses)
    public void Remove(CreatureLogic target, EffectParameters p)
    {
        target.ApplyBuff(-p.Amount, -p.SecondAmount);
    }
}
```

**Dans l'Inspector** — exemple "+1/+1 à toutes les créatures alliées dans ma zone" :
```
Effects (size: 1)
  [0]
    Effect:     <glisser StaticAura.asset ici>
    Trigger:    Aura
    Targeting:  AllFriendlyCreaturesInZone
    Parameters:
      Amount:        1    ← bonus d'attaque
      SecondAmount:  1    ← bonus de vie
      UseSecondAmount: ✓
```

### `SummonTokenSO.cs`
```csharp
[CreateAssetMenu(menuName = "Effects/SummonToken")]
public class SummonTokenSO : EffectSO
{
    public override void Execute(EffectContext ctx, TargetingType targeting, EffectParameters p)
    {
        if (p.TokenToSummon == null || ctx.Caster == null) return;
        for (int i = 0; i < p.Amount; i++)
        {
            switch (p.Placement)
            {
                case TokenPlacement.ToHand:
                    // Apparaît dans la main — le joueur le joue manuellement
                    ctx.Caster.GetACardNotFromDeck(p.TokenToSummon);
                    break;

                case TokenPlacement.ToSourceZone:
                    // Apparaît directement dans la zone de la créature source
                    // (ex : une créature meurt et un token la remplace dans la même zone)
                    PlayerArea zone = ctx.SourceCreature != null
                        ? ctx.Caster.GetPlayerAreaByID(ctx.SourceCreature.BaseID)
                        : ctx.Caster.MainPArea;
                    SpawnTokenOnBoard(ctx.Caster, p.TokenToSummon, zone);
                    break;

                case TokenPlacement.ToMainZone:
                    // Apparaît dans la zone principale du joueur
                    SpawnTokenOnBoard(ctx.Caster, p.TokenToSummon, ctx.Caster.MainPArea);
                    break;
            }
        }
    }

    private void SpawnTokenOnBoard(Player owner, CardAsset tokenAsset, PlayerArea area)
    {
        if (area == null) return;
        // Crée une CardLogic temporaire pour le token
        CardLogic tokenCard = new CardLogic(tokenAsset);
        tokenCard.owner = owner;
        // Joue la créature sans déduire de ressources (c'est un token gratuit)
        int tablePos = owner.table.CreaturesInPlay.Count;
        owner.PlayACreatureFromHand(tokenCard, tablePos, area);
        // Note : PlayACreatureFromHand déduit le coût — il faudra soit mettre
        // le coût du token à 0, soit ajouter une surcharge sans déduction.
    }
}
```

> **Note sur SpawnTokenOnBoard** : `PlayACreatureFromHand` déduit actuellement les ressources. Pour les tokens gratuits, la solution la plus simple est de créer un `CardAsset` de token avec `MainCost = 0` et `SecondCost = 0`. Sinon, on pourra ajouter une surcharge `PlayACreatureForFree()` dans `Player` lors de la Phase 3 si nécessaire.

---

## Modifications des fichiers existants

### `CardAsset.cs`
Ajouter à la fin (garder tous les anciens champs pour la migration) :
```csharp
[Header("Effects (nouveau système)")]
public List<CardEffectData> Effects = new List<CardEffectData>();
```

### `CreatureLogic.cs` — constructeur
Après le bloc de réflexion existant (lignes 137-141) :
```csharp
// Nouveau système
if (ca.Effects != null && ca.Effects.Count > 0)
    EffectProcessor.RegisterCreatureEffects(this, ca);
```

Ajouter la méthode `ApplyBuff` (nécessaire pour `GiveBuffSO`) :
```csharp
public void ApplyBuff(int attackDelta, int healthDelta)
{
    baseAttack += attackDelta;
    baseHealth += healthDelta;
    if (healthDelta > 0) health += healthDelta;
}
```

### `CreatureLogic.Die()` (ligne 153)
Après le bloc `if (effect != null)` existant :
```csharp
// Nouveau système
EffectProcessor.NotifyCreatureDied(this, owner);
```

### `Player.PlayACreatureFromHand()` (ligne 333)
Après `if (newCreature.effect != null) newCreature.effect.WhenACreatureIsPlayed();` (ligne 344) :
```csharp
// Nouveau système — battlecry
EffectProcessor.FireOnPlay(playedCard.ca, new EffectContext
{
    Caster = this,
    ExplicitTarget = null,
    SourceCreature = newCreature
});
// Nouveau système — met à jour les auras existantes avec la nouvelle créature
EffectProcessor.NotifyCreaturePlayed(newCreature);
```
**Idem** dans `NetworkFlushPlayCreature()` (ligne 397) et `NetworkPlayCreatureFromHand()` (ligne 428).

### `Player.PlayASpellFromHand()` (ligne 306)
Après `playedCard.effect.ActivateEffect(...)` (ligne 312) :
```csharp
// Nouveau système
EffectProcessor.FireOnPlay(playedCard.ca, new EffectContext
{
    Caster = this,
    ExplicitTarget = target
});
```

### `Player.OnTurnStart()` (ligne 165)
Au début de la méthode :
```csharp
EffectProcessor.NotifyTurnStart(this);
```

### `Player.OnTurnEnd()` (ligne 205)
Au début de la méthode :
```csharp
EffectProcessor.NotifyTurnEnd(this);
```

### `TurnManager.OnGameStart()` (ligne 47)
Avec les autres `.Clear()` (ligne 77-79) :
```csharp
EffectProcessor.Reset();
```

---

## Exemple concret : "Ghost Marine – Cri de bataille : Inflige 2 dégâts à toutes les créatures ennemies"

### Ancien système
1. Créer un nouveau fichier `DamageAllEnemyCreaturesBattlecry.cs`
2. Hériter de `CreatureEffect`, surcharger `WhenACreatureIsPlayed()`
3. Dans le CardAsset Inspector : `CreatureScriptName = "DamageAllEnemyCreaturesBattlecry"`, `specialCreatureAmount = 2`

### Nouveau système
Aucun nouveau fichier C#. Dans l'Inspector du `CardAsset` Ghost Marine :
```
Effects (size: 1)
  [0]
    Effect:     <glisser DealDamage.asset ici>
    Trigger:    OnPlay
    Targeting:  AllEnemyCreatures
    Parameters:
      Amount:   2
```

Pour ajouter "et aussi pioche 1 carte", ajouter un deuxième slot :
```
  [1]
    Effect:     <glisser DrawCards.asset ici>
    Trigger:    OnPlay
    Targeting:  Self
    Parameters:
      Amount:   1
```

---

## Migration des cartes existantes

Table de correspondance pour les effets déjà en place :

| Ancienne classe             | Nouvelle EffectSO   | Targeting           | Amount                   |
|-----------------------------|---------------------|---------------------|--------------------------|
| `DealDamageToTarget`        | `DealDamageSO`      | `TargetCreature`    | `specialSpellAmount`     |
| `GiveRessourcesBonus`       | `GiveResourcesSO`   | `Self`              | `specialSpellAmount`     |
| `DamageOpponentBattlecry`   | `DealDamageSO`      | `Opponent`          | `specialCreatureAmount`  |

---

## Plan de migration en 3 phases

### Phase 1 (fondation, non-bloquante)
- Créer tous les nouveaux fichiers
- Ajouter `List<CardEffectData> Effects` dans `CardAsset`
- Brancher les appels dans `Player`, `CreatureLogic`, `TurnManager`
- Les 26 cartes existantes ont `Effects` vide → elles continuent de fonctionner via l'ancien système

### Phase 2 (migration des cartes existantes)
- Pour chaque carte existante : configurer les `Effects` dans l'Inspector, vider les anciens champs `ScriptName`

### Phase 3 (nettoyage — uniquement quand toutes les cartes sont migrées)

**Condition** : toutes les cartes ont `Effects` configuré ET les champs `ScriptName` sont vides sur tous les assets.

**Fichiers à supprimer entièrement :**
```
Assets/Scripts/Logic/SpellScripts/
    SpellEffect.cs
    DealDamageToTarget.cs
    DamageAllOpponentCreatures.cs
    GiveRessourcesBonus.cs
    HeroPower2Face.cs
    HeroPowerDrawCardTakeDamage.cs

Assets/Scripts/Logic/CreatureScripts/
    CreatureEffect.cs
    DamageOpponentBattlecry.cs
    BiteOwner.cs
```
Puis supprimer les deux dossiers `SpellScripts/` et `CreatureScripts/` s'ils sont vides.

**Champs à supprimer dans `CardAsset.cs` :**
- `CreatureScriptName`, `specialCreatureAmount`
- `BuildingScriptName`, `specialBuildingAmount`
- `SpellScriptName`, `specialSpellAmount`

**Code mort à supprimer dans `Player.cs` :**
- Le bloc `if (newCreature.effect != null) newCreature.effect.WhenACreatureIsPlayed()` dans `PlayACreatureFromHand`, `NetworkFlushPlayCreature`, `NetworkPlayCreatureFromHand`
- Le bloc `if (playedCard.effect != null) playedCard.effect.ActivateEffect(...)` dans `PlayASpellFromHand`

**Code mort à supprimer dans `CreatureLogic.cs` :**
- Le champ `public CreatureEffect effect`
- Le bloc `if (ca.CreatureScriptName != null ...)` dans le constructeur
- Le bloc `if (effect != null) { effect.WhenACreatureDies(); effect.UnRegisterEventEffect(); }` dans `Die()`

**Code mort à supprimer dans `CardLogic.cs` :**
- Le champ `effect` (de type `SpellEffect`)
- Le bloc `if (ca.SpellScriptName != null ...)` dans le constructeur

---

## Intégration réseau

### Principe général

Le système d'effets **ne nécessite pas de synchronisation réseau propre**. Le principe est simple : si les deux clients reçoivent la même action via `ClientRpc` et exécutent le même code, `EffectProcessor` se déclenchera de façon identique des deux côtés.

```
Client A (caster)                Server                Client B
     |                              |                       |
     |-- PlaySpellServerRpc() ----->|                       |
     |                    PlaySpellClientRpc() ------------>|
     |<--- PlaySpellClientRpc() ----|                       |
     |                              |                       |
NetworkPlaySpellFromHand()      (même méthode)      NetworkPlaySpellFromHand()
EffectProcessor.FireOnPlay()                        EffectProcessor.FireOnPlay()
     ↓                                                      ↓
Effets exécutés identiquement sur les deux clients
```

### Effets déjà synchronisés automatiquement

| Trigger | Raison |
|---------|--------|
| `OnDeath` | `CreatureLogic.Die()` appelé quand la vie tombe à 0 — identique sur les deux clients |
| `OnTurnStart` | `Player.OnTurnStart()` déclenché par `PhaseTransitionClientRpc` — identique |
| `OnTurnEnd` | Idem via le même mécanisme |
| `OnAnyCreatureDies` | Déclenché dans `NotifyCreatureDied()` — déjà synchro |
| Battlecry (créatures) | `NetworkFlushPlayCreature()` est un `ClientRpc` — s'exécute sur les deux clients |
| Aura (register) | Déclenché dans le constructeur de `CreatureLogic`, appelé depuis `NetworkFlushPlayCreature()` |

### La seule vraie lacune : les sorts (bug existant)

Actuellement, `PlayASpellFromHand()` s'exécute **localement uniquement** — le joueur adverse ne voit pas l'effet. Ce n'est pas une nouveauté du plan, c'est un bug déjà présent.

La correction s'intègre naturellement au plan :

**Nouveau flux pour les sorts :**
```
Drag & Drop → Player.LocalPlaySpellFromHand(cardUniqueID, targetID)
                 └── GameNetworkManager.Instance.PlaySpellServerRpc(cardUniqueID, targetID, playerIndex)
                           └── Server: PlaySpellClientRpc(playerIndex, cardUniqueID, targetID)
                                        └── All clients: player.NetworkPlaySpellFromHand(cardUniqueID, targetID)
                                                           └── EffectProcessor.FireOnPlay(ca, ctx)
```

**Méthode à ajouter dans `Player.cs` :**
```csharp
public void NetworkPlaySpellFromHand(int cardUniqueID, int targetUniqueID)
{
    CardLogic playedCard = hand.GetCardByUniqueID(cardUniqueID);

    // Déduire les ressources (déjà fait pendant NetworkPending, mais nécessaire ici pour les sorts)
    MainRessourceAvailable -= playedCard.MainCost;
    SecondRessourceAvailable -= playedCard.SecondCost;

    // Reconstruire la cible depuis l'ID (créature ou bâtiment)
    ILivable explicitTarget = null;
    if (targetUniqueID > 0)
    {
        CreatureLogic.CreaturesCreatedThisGame.TryGetValue(targetUniqueID, out CreatureLogic creature);
        explicitTarget = creature;
        // TODO : ajouter BuildingLogic.BuildingsCreatedThisGame si ciblage bâtiment
    }

    // Ancien système (pendant la migration)
    if (playedCard.effect != null)
        playedCard.effect.ActivateEffect(playedCard.ca.specialSpellAmount, explicitTarget, this);

    // Nouveau système
    EffectProcessor.FireOnPlay(playedCard.ca, new EffectContext
    {
        Caster = this,
        ExplicitTarget = explicitTarget
    });

    new PlayASpellCardCommand(this, playedCard).AddToQueue();
    hand.CardsInHand.Remove(playedCard);
}
```

> **Pourquoi transmettre `targetUniqueID` et non la cible directement ?**
> Parce que le client B ne peut pas recevoir une référence d'objet du client A. Chaque client retrouve localement la cible depuis l'ID (qui est identique sur tous les clients grâce au serveur).

### Battlecry avec cible explicite (créatures)

Si une créature a un battlecry de type `TargetCreature` (ex: "Inflige 2 dégâts à une créature ciblée"), il faut aussi transmettre le `targetUniqueID`. Actuellement `PlayCreatureServerRpc` n'a pas ce paramètre.

**Ajout nécessaire dans `GameNetworkManager.cs` :**
```csharp
// Ajouter targetUniqueID = 0 comme paramètre optionnel
public void PlayCreatureServerRpc(int cardUniqueID, int tablePos, int baseID, int playerIndex, int targetUniqueID = 0)
```
Puis passer `targetUniqueID` jusqu'à `NetworkFlushPlayCreature()` pour construire l'`EffectContext`.

### Ce qui n'a PAS besoin de modification réseau

- `EffectProcessor` lui-même — aucun RPC dedans
- `EffectSO.Execute()` — ne touche que l'état local (qui est déjà synchro)
- `AuraInstance` — gérée localement, reconstruite identiquement sur les deux clients
- La full state broadcast en fin de phase reste le filet de sécurité contre toute désynchro résiduelle

---

## Vérification

Après implémentation, tester :
1. **Lancer une partie locale** — les cartes existantes fonctionnent encore (ancien système)
2. **Créer un CardAsset test** avec `DealDamageSO` (OnPlay, AllEnemyCreatures, Amount=2) — jouer la carte inflige bien 2 dégâts à toutes les créatures ennemies
3. **Tester un trigger réactif** : créer une carte avec `DealDamageSO` (OnDeath, Opponent, Amount=1) — quand la créature meurt, le joueur adverse perd 1 point de vie
4. **Tester OnTurnStart** : créer une carte avec `GiveResourcesSO` (OnTurnStart, Self, Amount=1) — le joueur gagne 1 ressource par tour tant que la créature est en jeu
5. **Tester en multijoueur** — vérifier que les effets se déclenchent des deux côtés de façon identique
