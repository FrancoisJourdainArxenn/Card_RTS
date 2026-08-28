using UnityEngine;

// Enfilée UNE SEULE FOIS par joueur par ZoneCombatResolver.EnqueueBattleCommands, après que toute la
// zone où sa base principale meurt ait fini d'être traitée (voir diedHomeBasePlayerIDs) — jamais
// directement par le setter Health, ni par MainBaseVisual.ApplyHealthDisplay à chaque coup. Bloque la
// file de commandes (donc la transition caméra vers la zone suivante, et un éventuel GameOverCommand
// déjà enfilé juste après) jusqu'à ce que l'animation de mort ait fini de jouer.
public class MainBaseDeathAnimationCommand : Command
{
    private readonly int playerID;

    public MainBaseDeathAnimationCommand(int playerID)
    {
        this.playerID = playerID;
    }

    public override void StartCommandExecution()
    {
        Player player = playerID == GlobalSettings.Instance.LowPlayer.PlayerID
            ? GlobalSettings.Instance.LowPlayer
            : GlobalSettings.Instance.TopPlayer;

        MainBaseVisual mbv = player?.baseVisual;
        if (mbv == null)
        {
            Debug.LogWarning($"[MainBaseDeathAnimationCommand] MainBaseVisual introuvable pour le joueur {playerID}.");
            CommandExecutionComplete();
            return;
        }

        mbv.PlayDeathAnimationAndHide(CommandExecutionComplete);
    }
}
