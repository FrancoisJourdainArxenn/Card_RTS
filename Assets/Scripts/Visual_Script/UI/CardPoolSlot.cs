using UnityEngine;
using UnityEngine.UI;

public class CardPoolSlot : MonoBehaviour
{
    public enum SlotKind { Main, Second }

    public CardPoolSO cardPool;

    [SerializeField] private SlotKind slotKind;
    [SerializeField] private Image icon;
    [SerializeField] private UITooltipTrigger tooltipTrigger;
    [SerializeField] private string tooltipLabel;

    // Coche sur les slots recap du hero selectionne (ex: FullDeckList_Panel) : le clic ouvre le
    // panneau de choix pour HeroPortrait.Selected au lieu d'appliquer immediatement cardPool.
    [SerializeField] private bool opensChoiceForSelectedHero;

    public SlotKind Kind => slotKind;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        RefreshVisual();
    }

    private void OnEnable()
    {
        DeckListPanelController.OnChoosingPoolChanged += ApplyTooltipState;
        ApplyTooltipState(DeckListPanelController.Instance != null && DeckListPanelController.Instance.IsChoosingPool);
    }

    private void OnDisable()
    {
        DeckListPanelController.OnChoosingPoolChanged -= ApplyTooltipState;
    }

    // Pendant que AvailableDecks est ouvert, cliquer ce slot affiche deja son contenu via
    // DeckList_Panel (OpenChoicePanel plus bas) : le tooltip generique n'a plus lieu d'etre.
    private void ApplyTooltipState(bool isChoosingPool)
    {
        if (tooltipTrigger == null)
            return;

        tooltipTrigger.enabled = !isChoosingPool;

        // Desactiver le composant coupe les futurs OnPointerEnter/OnPointerExit, mais pas
        // OnPointerExit pour le tooltip deja affiche au moment du clic : sans ce Hide explicite,
        // il reste bloque a l'ecran puisque plus personne ne viendra jamais le fermer.
        if (isChoosingPool)
            UITooltip.HideTooltip_Static();
    }

    private void RefreshVisual()
    {
        Debug.Log($"{name}: cardPool={cardPool}, icon={icon}");
        if (icon != null && cardPool != null)
            icon.sprite = cardPool.cardPoolIcon;

        if (tooltipTrigger != null && cardPool != null)
        {
            string prefix = string.IsNullOrEmpty(tooltipLabel) ? cardPool.poolName : $"{tooltipLabel} {cardPool.poolName}";
            tooltipTrigger.PrependText(prefix);
        }
    }

    // Remplace le pool de ce slot (ex: confirmation d'un choix dans AvailableDecks) et
    // rafraichit son icone/tooltip en consequence.
    public void SetCardPool(CardPoolSO pool)
    {
        cardPool = pool;
        RefreshVisual();
    }

    // Certains pools Main/Second partagent volontairement le meme poolName (voir
    // DeckListPanelController.RefreshPoolAvailability) : les deux ne peuvent pas etre choisis en
    // meme temps, ce candidat est donc grise/non cliquable tant que l'autre cote le porte deja.
    public void SetSelectable(bool selectable)
    {
        if (button != null)
            button.interactable = selectable;
        if (icon != null)
            icon.color = selectable ? Color.white : new Color(1f, 1f, 1f, 0.35f);
    }

    // A cabler sur le OnClick du Button. Slot recap du hero selectionne (opensChoiceForSelectedHero) :
    // ouvre le panneau de choix pour HeroPortrait.Selected. Entree candidate de Row 2/Row 3 : applique
    // ce pool tout de suite, selon slotKind.
    public void OpenChoicePanel()
    {
        if (opensChoiceForSelectedHero)
        {
            HeroPortrait selected = HeroPortrait.Selected;
            FactionAsset faction = selected != null && selected.deck != null && selected.deck.heroCard != null
                ? selected.deck.heroCard.Faction
                : null;
            if (selected != null && faction != null)
                DeckListPanelController.Instance.ShowPoolChoices(faction, selected);
        }
        else
        {
            DeckListPanelController.Instance.ApplyPoolChoice(slotKind, cardPool);
        }

        // Le clic remplace desormais le survol pour afficher/rafraichir le contenu du pool dans
        // DeckList_Panel (utile surtout pour les entrees candidates de Row 2/Row 3).
        if (DeckListPanelController.Instance != null && DeckListPanelController.Instance.IsChoosingPool)
            DeckListPanelController.Instance.ShowDeckList(cardPool);
    }
}
