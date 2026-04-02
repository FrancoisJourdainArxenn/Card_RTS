using UnityEngine;
using System.Collections;

public class HeroPower2Face : SpellEffect 
{

    public override void ActivateEffect(int specialAmount = 0, ILivable target = null, Player caster = null)
    {
        if (caster == null)
            return;
        Player opp = caster.otherPlayer;
        new DealDamageCommand(opp.PlayerID, 2, opp.Health - 2).AddToQueue();
        opp.Health -= 2;
    }
}
