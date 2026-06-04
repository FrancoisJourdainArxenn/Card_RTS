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
    int _lastIncome = int.MinValue;

    void OnEnable()
    {
        lastShownCount = int.MinValue;
        _lastIncome = int.MinValue;
    }

    void Start()
    {
        ResolvePlayer();
        lastShownCount = int.MinValue;
        _lastIncome = int.MinValue;
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

        int ressource = player.mainRessourceAvailable;
        if (mainRessourceText != null)
        {
            mainRessourceText.text = ressource.ToString();
            if (ressource > lastShownCount && lastShownCount != int.MinValue)
                ValuePopAnimation.Pop(mainRessourceText.transform);
            lastShownCount = ressource;
        }

        int income = player.playerMainIncome;
        if (MainRessourceIncomeText != null)
        {
            MainRessourceIncomeText.text = income.ToString();
            if (income > _lastIncome && _lastIncome != int.MinValue)
                ValuePopAnimation.Pop(MainRessourceIncomeText.transform);
            _lastIncome = income;
        }
    }


}
