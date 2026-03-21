using TMPro;
using UnityEngine;

/// <summary>
/// Affiche le nombre de cartes restantes dans le deck du joueur (référence <see cref="Deck.cards"/>).
/// À placer sur l’icône UI ; assigner le TextMeshPro et le joueur (ou <see cref="owner"/> comme pour HandVisual).
/// </summary>
public class DeckVisual : MonoBehaviour
{
    [Tooltip("Texte au centre de l’icône (ex. le « X » de maquette).")]
    [SerializeField] TMP_Text countText;

    [Tooltip("Si renseigné, prend la priorité sur owner.")]
    [SerializeField] Player player;

    [Tooltip("Utilisé si player n’est pas assigné ; même convention que HandVisual.")]
    [SerializeField] AreaPosition owner = AreaPosition.Low;

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
        if (player != null)
            return;
        if (GlobalSettings.Instance != null && GlobalSettings.Instance.Players.TryGetValue(owner, out Player p))
            player = p;
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
}
