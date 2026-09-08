using UnityEngine;
using UnityEngine.SceneManagement;

public class SoloConnectionPanel : MonoBehaviour
{
    [SerializeField] private string battleSceneName = "BattleScene";

    public void LaunchGame()
    {
        NetworkSessionData.IsNetworkSession = false;
        NetworkSessionData.IsVsAI = true;
        SceneManager.LoadScene(battleSceneName);
    }
}
