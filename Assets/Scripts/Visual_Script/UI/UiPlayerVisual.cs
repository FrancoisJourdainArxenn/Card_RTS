using TMPro;
using UnityEngine;

public class UiPlayerVisual : MonoBehaviour
{
    [Tooltip("Texte au centre de l’icône (ex. le « X » de maquette).")]
    [SerializeField] TMP_Text countText;
    public TMP_Text mainRessourceText,secondRessourceText;
    public TMP_Text MainRessourceIncomeText,SecondRessourceIncomeText;
    public TMP_Text UiHealthText; 

    [Tooltip("Si renseigné, prend la priorité sur owner.")]
    [SerializeField] Player player;

    int lastShownCount = int.MinValue;

    void OnEnable()
    {
        lastShownCount = int.MinValue;
        Refresh(force: true);
    }

    void Start()
    {
        ResolvePlayer();
        lastShownCount = int.MinValue;
        Refresh(force: true);
    }

    void LateUpdate()
    {
        Refresh(force: false);
    }
    void ResolvePlayer()
    {
        if (GlobalSettings.Instance?.localPlayer != null)
            player = GlobalSettings.Instance.localPlayer;
    }

    void Refresh(bool force)
    {
        if (countText == null)
            return;
        if (player == null)
            ResolvePlayer();

        int n = (player != null && player.deck != null) ? player.deck.cards.Count : 0;
        if (!force && n == lastShownCount)
            return;
        lastShownCount = n;
        countText.text = n.ToString();
    }
    public void RefreshUI()
    {
        ResolvePlayer();
        if (player == null) return;

        if (mainRessourceText != null)
            mainRessourceText.text = player.mainRessourceAvailable.ToString();
        if (secondRessourceText != null)
            secondRessourceText.text = player.secondRessourceAvailable.ToString();
        if (MainRessourceIncomeText != null)
            MainRessourceIncomeText.text = player.playerMainIncome.ToString();
        if (SecondRessourceIncomeText != null)
            SecondRessourceIncomeText.text = player.playerSecondIncome.ToString();
        if (UiHealthText != null)
            UiHealthText.text = player.Health.ToString();
    }

}
