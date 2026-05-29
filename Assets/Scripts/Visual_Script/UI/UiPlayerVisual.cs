using TMPro;
using UnityEngine;

public class UiPlayerVisual : MonoBehaviour
{
    [Tooltip("Texte au centre de l’icône (ex. le « X » de maquette).")]

    public TMP_Text mainRessourceText;
    public TMP_Text MainRessourceIncomeText;

    [Tooltip("Si renseigné, prend la priorité sur owner.")]
    [SerializeField] Player player;

    int lastShownCount = int.MinValue;

    void OnEnable()
    {
        lastShownCount = int.MinValue;
    }

    void Start()
    {
        ResolvePlayer();
        lastShownCount = int.MinValue;
    }


    void ResolvePlayer()
    {
        if (GlobalSettings.Instance?.localPlayer != null)
            player = GlobalSettings.Instance.localPlayer;
    }


    public void RefreshUI()
    {
        ResolvePlayer();
        if (player == null) return;

        if (mainRessourceText != null)
            mainRessourceText.text = player.mainRessourceAvailable.ToString();
        if (MainRessourceIncomeText != null)
            MainRessourceIncomeText.text = player.playerMainIncome.ToString();
    }

}
