using UnityEngine;

public class ApplyShieldCommand : Command
{
    private readonly int targetID;
    private readonly int amount;
    private readonly EffectVisualData visualData;

    public ApplyShieldCommand(int targetID, int amount, EffectVisualData visualData)
    {
        this.targetID = targetID;
        this.amount = amount;
        this.visualData = visualData;
    }

    public override void StartCommandExecution()
    {
        GameObject target = IDHolder.GetGameObjectWithID(targetID);

        // "amount" est le DELTA de ce gain (voir ApplyShieldSO.ApplyToTarget), pas un total absolu :
        // AddShieldVfx l'ajoute à ce qui est déjà affiché (créant la bulle au premier gain) plutôt que
        // d'écraser avec un total capturé côté logique — celui-ci peut être bien plus avancé, toute la
        // planification d'un round de combat tournant avant que la moindre commande visuelle ne joue.
        if (target != null && target.TryGetComponent(out VfxManager vfx))
        {
            if (amount > 0)
                vfx.AddShieldVfx(visualData?.vfxPrefab, amount);
            else
                vfx.HideShieldVfx();
        }
        else
        {
            Debug.LogWarning($"[Shield/VFX] ApplyShieldCommand — objet visuel introuvable pour targetID={targetID} (amount={amount}) — affichage NON mis à jour, désync probable");
        }

        CommandExecutionComplete();
    }
}
