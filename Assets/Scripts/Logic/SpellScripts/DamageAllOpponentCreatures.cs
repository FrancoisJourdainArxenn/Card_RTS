using UnityEngine;
using System.Collections;

public class DamageAllOpponentCreatures : SpellEffect {

    public override void ActivateEffect(int specialAmount = 0, ILivable target = null, Player caster = null)
    {
        if (caster == null)
            return;
        CreatureLogic[] CreaturesToDamage = caster.otherPlayer.table.CreaturesInPlay.ToArray();
        foreach (CreatureLogic cl in CreaturesToDamage)
        {
            new DealDamageCommand(cl.ID, specialAmount, healthAfter: cl.Health - specialAmount).AddToQueue();
            cl.Health -= specialAmount;
        }
    }
}
