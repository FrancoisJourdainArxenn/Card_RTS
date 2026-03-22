using UnityEngine;
using System.Collections;

public class HeroPowerDrawCardTakeDamage : SpellEffect {

    public override void ActivateEffect(int specialAmount = 0, ICharacter target = null, Player caster = null)
    {
        if (caster == null)
            return;
        new DealDamageCommand(caster.PlayerID, 2, caster.Health - 2).AddToQueue();
        caster.Health -= 2;
        caster.DrawACard();
    }
}
