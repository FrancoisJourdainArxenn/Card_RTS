using UnityEngine;

// Barrière pure : bloque la LECTURE de la file (CommandExecutionComplete) jusqu'à ce que la
// BattleCam ait atteint la zone, sans jamais retarder l'AJOUT des popups OnBattleStart à la file
// (celui-ci reste synchrone, voir ZoneCombatResolver.EnqueueBattleCommands) — les popups doivent
// être enqueue AVANT les commandes d'attaque de la zone, dans le même passage synchrone, sinon
// FocusBattleCamOn (asynchrone) laisse le temps au foreach d'EnqueueBattleCommands d'empiler les
// attaques en premier pendant que la caméra est encore en train de transitionner.
public class ZoneBattleStartRevealCommand : Command
{
    private readonly Vector3 focusPosition;

    public ZoneBattleStartRevealCommand(Vector3 focusPosition)
    {
        this.focusPosition = focusPosition;
    }

    public override void StartCommandExecution()
    {
        if (CameraController.Instance != null)
            CameraController.Instance.FocusBattleCamOn(focusPosition, CommandExecutionComplete);
        else
            CommandExecutionComplete();
    }
}
