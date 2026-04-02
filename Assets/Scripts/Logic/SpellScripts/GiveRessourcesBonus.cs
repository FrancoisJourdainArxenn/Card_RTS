using UnityEngine;
using System.Collections;

public class GiveRessourcesBonus: SpellEffect 
{
    public override void ActivateEffect(int specialAmount = 0, ILivable target = null, Player caster = null)
    {
        if (caster != null)
            caster.GetBonusRessources(specialAmount, 0);
    }
}