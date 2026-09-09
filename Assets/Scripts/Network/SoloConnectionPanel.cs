using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class SoloConnectionPanel : MonoBehaviour
{
    [SerializeField] private string battleSceneName = "BattleScene";
    [SerializeField] private TMP_Dropdown mapDropdown;
    [SerializeField] private MenuRegistry menuRegistry;

    public void LaunchGame()
    {
        NetworkSessionData.IsNetworkSession = false;
        NetworkSessionData.IsVsAI = true;

        if (mapDropdown != null && menuRegistry != null)
        {
            int idx = mapDropdown.value;
            NetworkSessionData.SelectedMapIndex = idx == 0
                ? Random.Range(0, menuRegistry.maps.Length)
                : idx - 1;
        }

        NetworkSessionData.SelectedDeckPresetIndex = GetSelectedDeckPresetIndex();

        SceneManager.LoadScene(battleSceneName);
    }

    private int GetSelectedDeckPresetIndex()
    {
        DeckSO selected = HeroPortrait.SelectedDeck;
        if (selected == null || menuRegistry == null)
            return -1;

        return System.Array.IndexOf(menuRegistry.decks, selected);
    }
}
