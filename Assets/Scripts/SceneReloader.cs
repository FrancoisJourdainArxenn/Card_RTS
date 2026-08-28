using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class SceneReloader: MonoBehaviour {

    public void ReloadScene()
    {
        // Command has some static members, so let`s make sure that there are no commands in the Queue
        // reset all card and creature IDs
        IDFactory.ResetIDs();
        IDHolder.ClearIDHoldersList();
        CardLogic.CardsCreatedThisGame.Clear();
        CreatureLogic.CreaturesCreatedThisGame.Clear();
        BuildingLogic.BuildingsCreatedThisGame.Clear();
        Command.CommandQueue.Clear();
        Command.ClearDeferredState();
        Command.CommandExecutionComplete();
        CreatureAttackVisual.ResetFlightCounter();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
