using UnityEngine;
using System.Collections;

public class GiveRessourcesBonus: SpellEffect 
{
    public override void ActivateEffect(int specialAmount = 0, ICharacter target = null)
    {
        TurnManager.Instance.whoseTurn.GetBonusRessources(specialAmount, 0);
    }
}