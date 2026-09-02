using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class GameOverCommand : Command
{
    public readonly int WinnerPlayerID; // -1 si égalité
    public readonly bool IsDraw;

    const float ReturnToMenuDelay = 5f;

    public GameOverCommand(int winnerPlayerID, bool isDraw)
    {
        WinnerPlayerID = winnerPlayerID;
        IsDraw = isDraw;
    }

    public override void StartCommandExecution()
    {
        // Bloque les deux joueurs — même effet que l'ancien Player.Die(), maintenant appelé
        // explicitement ici plutôt que réactivement depuis le setter Health.
        GlobalSettings.Instance.LowPlayer.Die();

        // GameOverCommand tourne à l'identique sur chaque machine (voir ZoneCombatResolver.
        // EnqueueOrderedBattleCommands) — le message affiché est donc relatif au joueur local DE
        // CETTE machine, pas une valeur transmise telle quelle. TODO: remplacer ce message texte par
        // un vrai écran de fin de partie — WinnerPlayerID/IsDraw restent disponibles pour ça.
        string message = IsDraw ? "Tie"
            : GlobalSettings.Instance.localPlayer.PlayerID == WinnerPlayerID ? "Victory"
            : "Defeat";
        MessageManager.Instance.ShowMessage(message, Mathf.Infinity);
        Debug.Log(IsDraw ? "[GameOver] Match nul." : $"[GameOver] Le joueur {WinnerPlayerID} gagne.");

        // Command n'est pas un MonoBehaviour — la coroutine de retour au menu est hébergée sur
        // TurnManager.Instance, qui reste vivant jusqu'au changement de scène.
        if (TurnManager.Instance != null)
            TurnManager.Instance.StartCoroutine(ReturnToMainMenuAfterDelay(ReturnToMenuDelay));

        CommandExecutionComplete();
    }

    // Même séquence de nettoyage que PauseMenuController.GoToMainMenu (dupliquée ici plutôt que
    // partagée — même convention déjà suivie par SceneReloader.ReloadScene pour son propre
    // nettoyage). Tourne indépendamment sur chaque machine : pas de RPC, chacune revient à son
    // propre menu localement une fois le délai écoulé.
    static IEnumerator ReturnToMainMenuAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        IDFactory.ResetIDs();
        IDHolder.ClearIDHoldersList();
        CardLogic.CardsCreatedThisGame.Clear();
        CreatureLogic.CreaturesCreatedThisGame.Clear();
        BuildingLogic.BuildingsCreatedThisGame.Clear();
        CreatureLogic.PendingDeathList.Clear();
        BuildingLogic.PendingDeathVisualQueue.Clear();
        Command.CommandQueue.Clear();
        Command.ClearDeferredState();
        Command.CommandExecutionComplete();
        NetworkSessionData.IsNetworkSession = false;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        SceneManager.LoadScene("MenuScene");
    }
}
