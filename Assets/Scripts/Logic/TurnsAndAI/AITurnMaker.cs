using UnityEngine;
using System.Collections;

public class AITurnMaker : TurnMaker
{
    /*public override void OnRegroupPhaseStart()
    {
        base.OnRegroupPhaseStart();
        StartCoroutine(CoWaitQueueIdleThenEndPhase());
    }

    public override void OnCommandPhaseEntered()
    {
        new ShowMessageCommand("Enemy — Command", 1.2f).AddToQueue();
        StartCoroutine(CoAICommandPhase());
    }

    public override void OnBattlePhaseEntered()
    {
        new ShowMessageCommand("Enemy — Battle", 1.2f).AddToQueue();
        StartCoroutine(CoAIBattlePhase());
    }

    public override void OnEndPhaseEntered()
    {
        StartCoroutine(CoWaitQueueIdleThenEndPhase());
    }

    IEnumerator CoWaitQueueIdleThenEndPhase()
    {
        yield return new WaitWhile(() => Command.playingQueue || Command.CardDrawPending());
        yield return new WaitForSeconds(0.35f);
        if (TurnManager.Instance != null)
            TurnManager.Instance.RegisterEndPhase(p);
    }

    IEnumerator CoAICommandPhase()
    {
        while (true)
        {
            if (Command.CardDrawPending())
            {
                yield return null;
                continue;
            }
            if (!PlayACardFromHand())
                break;
            InsertDelay(1.5f);
            yield return null;
        }

        InsertDelay(1f);
        yield return StartCoroutine(CoWaitQueueIdleThenEndPhase());
    }

    IEnumerator CoAIBattlePhase()
    {
        bool strategyAttackFirst = Random.Range(0, 2) == 0;
        while (true)
        {
            if (Command.CardDrawPending())
            {
                yield return null;
                continue;
            }
            if (!MakeOneBattleMove(strategyAttackFirst))
                break;
            InsertDelay(1f);
            yield return null;
        }

        InsertDelay(1f);
        yield return StartCoroutine(CoWaitQueueIdleThenEndPhase());
    }

    bool MakeOneBattleMove(bool attackFirst)
    {
        if (attackFirst)
            return AttackWithACreature() || PlayACardFromHand();
        return PlayACardFromHand() || AttackWithACreature();
    }

    bool PlayACardFromHand()
    {
        foreach (CardLogic c in p.hand.CardsInHand)
        {
            if (c.CanBePlayed)
            {
                if (c.ca.MaxHealth == 0)
                {
                    if (c.ca.Targets == TargetingOptions.NoTarget)
                    {
                        p.PlayASpellFromHand(c, null);
                        InsertDelay(1.5f);
                        return true;
                    }
                }
                else
                {
                    p.PlayACreatureFromHand(c, 0);
                    InsertDelay(1.5f);
                    return true;
                }
            }
        }
        return false;
    }

    bool AttackWithACreature()
    {
        foreach (CreatureLogic cl in p.table.CreaturesOnTable)
        {
            if (cl.AttacksLeftThisTurn > 0)
            {
                if (p.otherPlayer.table.CreaturesOnTable.Count > 0)
                {
                    int index = Random.Range(0, p.otherPlayer.table.CreaturesOnTable.Count);
                    CreatureLogic targetCreature = p.otherPlayer.table.CreaturesOnTable[index];
                    cl.AttackCreature(targetCreature);
                }
                else
                    cl.GoFace();

                InsertDelay(1f);
                return true;
            }
        }
        return false;
    }

    void InsertDelay(float delay)
    {
        new DelayCommand(delay).AddToQueue();
    }*/
}
