using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[System.Serializable]
public class BuildingLogic : ILivable
{
    public Player owner;
    public CardAsset ca;
    public int UniqueBuildingID;

    // Compteur pour CondCounter (source = WrappedCondition) — vit sur l'instance (pas sur le
    // ConditionSO, stateless/partagé entre parties), clé par condition pour supporter plusieurs
    // compteurs indépendants.
    private Dictionary<ConditionSO, int> _conditionCounters;
    public int IncrementConditionCounter(ConditionSO key)
    {
        _conditionCounters ??= new Dictionary<ConditionSO, int>();
        _conditionCounters.TryGetValue(key, out int current);
        current++;
        _conditionCounters[key] = current;
        return current;
    }
    // Lecture seule, pour l'UI (texte de progression) — contrairement à IncrementConditionCounter,
    // n'avance pas le compteur.
    public int PeekConditionCounter(ConditionSO key) =>
        _conditionCounters != null && _conditionCounters.TryGetValue(key, out int v) ? v : 0;
    public BuildSpotVisual OriginSpot { get; set; }
    public int OriginZoneID { get; private set; }

    public int ID => UniqueBuildingID;
    public string DisplayName => ca.name;
    public ZoneLogic Zone => OriginSpot.Zone.Logic;

    private readonly int baseHealth;
    private int permMaxHealthBonus;
    private int tempMaxHealthBonus;
    public int MaxHealth
    {
        get => baseHealth + permMaxHealthBonus + tempMaxHealthBonus;
        set => permMaxHealthBonus = value - baseHealth - tempMaxHealthBonus;
    }

    private int health;
    public int Health
    {
        get => health;
        set
        {
            if (value > MaxHealth)
                health = MaxHealth;
            else if (value <= 0)
                Die();
            else
                health = value;
        }
    }

    private readonly int baseAttack;
    private int permAttackBonus;
    private int tempAttackBonus;
    public int Attack
    {
        get => baseAttack + permAttackBonus + tempAttackBonus;
        set => permAttackBonus = value - baseAttack - tempAttackBonus;
    }

    private int attacksForOneTurn = 0;
    private int activationForOneTurn = 0;
    public int AttacksLeftThisTurn { get; set; }
    public int ActivationLeftThisTurn {get; set; }

    public bool IsMelee => ca.melee;

    public bool Targetable => true;

    public bool CanAttack
    {
        get
        {
            bool battlePhase = TurnManager.Instance != null && TurnManager.Instance.IsBattlePhase;
            bool ownersTurn = battlePhase && TurnManager.Instance.MayPlayerUseControlsInPhase(owner);
            return ownersTurn && AttacksLeftThisTurn > 0;
        }
    }

    public BuildingLogic(Player owner, CardAsset ca, BuildSpotVisual originSpot, int networkID = -1)
    {
        this.ca = ca;
        this.owner = owner;
        this.OriginSpot = originSpot;
        this.OriginZoneID = originSpot.Zone.Logic.ID;
        baseHealth = ca.MaxHealth;
        health = ca.MaxHealth;
        baseAttack = ca.Attack;
        Attack = baseAttack;
        attacksForOneTurn = ca.AttacksForOneTurn;
        // activationForOneTurn = ca.ActivationsForOneTurn;
        UniqueBuildingID = networkID >= 0 ? networkID : IDFactory.GetUniqueID();
        BuildingsCreatedThisGame.Add(UniqueBuildingID, this);
        if (ca.Effects != null && ca.Effects.Count > 0)
            EffectRegistry.RegisterBuildingEffects(this, ca);
    }

    public void OnTurnStart()
    {
        AttacksLeftThisTurn = attacksForOneTurn;
        ActivationLeftThisTurn = activationForOneTurn;
    }

    public static List<BuildingLogic> PendingDeathVisualQueue = new List<BuildingLogic>();

    // Comme CreatureLogic.MarkPendingDeath : si on est dans un Command.RunDeferred (OnBattleStart),
    // la clé de report doit être capturée MAINTENANT, pas au moment où QueuePendingDeathVisuals()
    // sera éventuellement appelée par une autre zone — voir le commentaire détaillé sur MarkPendingDeath.
    public void Die()
    {
        owner.playedCards.Buildings.Remove(this);
        EffectRegistry.NotifyBuildingDied(this, owner);

        if (Command.DeferForBattleReplay)
        {
            int deferKey = Command.CurrentDeferSourceID ?? UniqueBuildingID;
            BuildingDieCommand dieCommand = new BuildingDieCommand(UniqueBuildingID);
            Command.DeferDeath(deferKey, dieCommand.AddToQueueImmediate);
        }
        else
        {
            PendingDeathVisualQueue.Add(this);
        }
    }

    // Met en file la BuildingDieCommand de tout bâtiment marqué mort EN DEHORS d'un contexte différé
    // depuis le dernier appel. Appelé par l'orchestrateur juste après avoir mis en file ses propres
    // commandes "cause" (voir Command.FlushPendingDeaths).
    public static void QueuePendingDeathVisuals()
    {
        foreach (BuildingLogic building in PendingDeathVisualQueue)
            new BuildingDieCommand(building.UniqueBuildingID).AddToQueue();
        PendingDeathVisualQueue.Clear();
    }

    // Équivalent de CreatureLogic.ResolveBattleStartEffects, pour les bâtiments. Pas de notion
    // de "pending death" côté bâtiment (Die() est immédiat) : on vérifie juste que ce bâtiment
    // est toujours dans playedCards.Buildings avant de résoudre son effet.
    public void ResolveBattleStartEffects(int zoneDeferKey)
    {
        if (!owner.playedCards.Buildings.Contains(this)) return; // tué par un OnBattleStart précédent
        if (ca.Effects == null) return;

        bool isNetworkServer = NetworkSessionData.IsNetworkSession && NetworkManager.Singleton.IsServer;
        for (int i = 0; i < ca.Effects.Count; i++)
        {
            CardEffectData data = ca.Effects[i];
            if (data.Trigger != TriggerType.OnBattleStart) continue;

            try
            {
                if (!NetworkSessionData.IsNetworkSession)
                {
                    Command.RunDeferred(zoneDeferKey, () =>
                        EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                }
                else if (isNetworkServer)
                {
                    int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                    try
                    {
                        EffectSO.SetNetworkRng(new System.Random(seed));
                        Command.RunDeferred(zoneDeferKey, () =>
                            EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
                    }
                    finally
                    {
                        EffectSO.ClearNetworkRng();
                    }
                    ZoneCombatResolver.RecordOnBattleStartReplay(zoneDeferKey, UniqueBuildingID, true, i, seed);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OnBattleStart] Exception pendant l'effet #{i} ({data.EffectName}) sur {DisplayName} (ID:{UniqueBuildingID}) : {e}");
            }
        }
    }

    public void ReplayBattleStartEffect(int zoneDeferKey, int effectIndex, int seed)
    {
        if (ca.Effects == null || effectIndex < 0 || effectIndex >= ca.Effects.Count) return;
        CardEffectData data = ca.Effects[effectIndex];
        if (data.Trigger != TriggerType.OnBattleStart) return;

        try
        {
            EffectSO.SetNetworkRng(new System.Random(seed));
            Command.RunDeferred(zoneDeferKey, () =>
                EffectRegistry.Execute(data, new EffectContext { Caster = owner, Source = this }));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[OnBattleStart][Replay] Exception pendant le rejeu de l'effet #{effectIndex} ({data.EffectName}) sur {DisplayName} (ID:{UniqueBuildingID}) : {e}");
        }
        finally
        {
            EffectSO.ClearNetworkRng();
        }
    }

    // public void AttackCreature(CreatureLogic target)
    // {
    //     AttacksLeftThisTurn--;
    //     int targetHealthAfter = target.Health - Attack;
    //     int attackerHealthAfter = Health - target.Attack;
    //     new BuildingAttackCommand(target.UniqueCreatureID, UniqueBuildingID, target.Attack, Attack, attackerHealthAfter, targetHealthAfter).AddToQueue();
    //     target.Health -= Attack;
    //     Health -= target.Attack;
    // }

    // public void AttackCreatureWithID(int uniqueCreatureID)
    // {
    //     AttackCreature(CreatureLogic.CreaturesCreatedThisGame[uniqueCreatureID]);
    // }

    // public void AttackBase(BaseLogic target)
    // {
    //     AttacksLeftThisTurn--;
    //     int targetHealthAfter = target.Health - Attack;
    //     new BuildingAttackCommand(target.ID, UniqueBuildingID, 0, Attack, Health, targetHealthAfter).AddToQueue();
    //     target.Health -= Attack;
    // }

    // public void AttackBaseWithID(int uniqueBaseID)
    // {
    //     AttackBase(BaseLogic.BasesCreatedThisGame[uniqueBaseID]);
    // }

    // public void AttackBuilding(BuildingLogic target)
    // {
    //     AttacksLeftThisTurn--;
    //     int targetHealthAfter = target.Health - Attack;
    //     int attackerHealthAfter = Health - target.Attack;
    //     new BuildingAttackCommand(target.UniqueBuildingID, UniqueBuildingID, target.Attack, Attack, attackerHealthAfter, targetHealthAfter).AddToQueue();
    //     target.Health -= Attack;
    //     Health -= target.Attack;
    // }

    // public void AttackBuildingWithID(int uniqueBuildingID)
    // {
    //     AttackBuilding(BuildingsCreatedThisGame[uniqueBuildingID]);
    // }

    public static Dictionary<int, BuildingLogic> BuildingsCreatedThisGame = new Dictionary<int, BuildingLogic>();
}
