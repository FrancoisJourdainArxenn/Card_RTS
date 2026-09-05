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

        // "amount" est le total de bouclier capturé au moment où il a réellement été accordé (voir
        // ApplyShieldSO.ApplyToTarget) — pas relu en direct ici : un combat prédit plus loin dans la
        // même séquence peut avoir déjà entièrement consommé ce bouclier avant que cette commande ne
        // rejoue, et le lire en direct afficherait alors 0 (bouclier jamais visible). La consommation,
        // elle, rejoue séparément via ConsumeShieldCommand, mise en file après celle-ci.
        if (target != null && target.TryGetComponent(out VfxManager vfx))
        {
            if (amount > 0)
                vfx.ShowShieldVfx(visualData?.vfxPrefab, amount);
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
