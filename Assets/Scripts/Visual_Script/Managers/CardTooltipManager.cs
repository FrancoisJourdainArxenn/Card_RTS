using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CardTooltipManager : MonoBehaviour
{
    public static CardTooltipManager Instance;

    [Header("Mini card prefabs (même logique de choix que CardPreviewUI)")]
    public GameObject cardTooltipPrefab;       // fallback (Unit / Building)
    public GameObject actionCardTooltipPrefab;
    public GameObject heroCardTooltipPrefab;

    public Transform tooltipContainer;
    public float tooltipDelay = 1f;
    public float fadeInDuration = 0.3f;
    public float fadeOutDuration = 0.15f;
    [SerializeField] private Vector2 tooltipMargin = new(20f, 0f); // espace entre la colonne mots-clés et celle-ci
    [SerializeField] private float miniCardScale = 0.55f;

    public RectTransform TooltipRect => (RectTransform)tooltipContainer;

    private CanvasGroup canvasGroup;
    private readonly List<GameObject> _activePanels = new();

    void Awake()
    {
        Instance = this;
        canvasGroup = tooltipContainer.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0;
    }

    private GameObject GetPrefabForCard(CardAsset asset)
    {
        return (asset.IsHero && heroCardTooltipPrefab != null) ? heroCardTooltipPrefab
            : (asset.Type == CardType.Action && actionCardTooltipPrefab != null) ? actionCardTooltipPrefab
            : cardTooltipPrefab;
    }

    public void BuildCardTooltips(List<CardAsset> referencedCards)
    {
        canvasGroup.DOKill();
        ClearPanels();

        if (referencedCards == null || referencedCards.Count == 0) return;

        foreach (CardAsset card in referencedCards)
        {
            if (card == null) continue;
            GameObject prefab = GetPrefabForCard(card);
            if (prefab == null) continue;

            GameObject panel = Instantiate(prefab, tooltipContainer);
            OneCardManager manager = panel.GetComponent<OneCardManager>();
            manager.cardAsset = card;
            manager.ReadCardFromAsset();

            // VerticalLayoutGroup calcule l'espace réservé à partir du RectTransform, pas du localScale :
            // sans LayoutElement explicite, la colonne laisserait un vide à la taille non réduite.
            RectTransform panelRect = (RectTransform)panel.transform;
            Vector2 fullSize = panelRect.rect.size;
            panel.transform.localScale = Vector3.one * miniCardScale;
            LayoutElement layoutElement = panel.GetComponent<LayoutElement>() ?? panel.AddComponent<LayoutElement>();
            layoutElement.preferredWidth  = fullSize.x * miniCardScale;
            layoutElement.preferredHeight = fullSize.y * miniCardScale;

            _activePanels.Add(panel);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(TooltipRect);
    }

    // S'ancre à droite de la colonne mots-clés en lisant sa largeur réelle (post-rebuild), pour
    // rester "à côté" d'elle quel que soit son nombre d'entrées.
    public void AnchorToCard(Vector2 cardAnchoredPosition)
    {
        ReminderTextManager reminders = ReminderTextManager.Instance;
        float afterKeywordColumn = 0f;
        if (reminders != null && reminders.TooltipRect.childCount > 0)
            afterKeywordColumn = reminders.TooltipOffset.x + reminders.TooltipRect.rect.width;

        TooltipRect.anchoredPosition = cardAnchoredPosition + new Vector2(afterKeywordColumn, 0f) + tooltipMargin;
    }

    public void FadeIn()
    {
        canvasGroup.alpha = 0;
        canvasGroup.DOFade(1f, fadeInDuration).SetDelay(tooltipDelay);
    }

    public void HideTooltips()
    {
        canvasGroup.DOKill();
        canvasGroup.DOFade(0f, fadeOutDuration).OnComplete(ClearPanels);
    }

    private void ClearPanels()
    {
        foreach (GameObject p in _activePanels)
            Destroy(p);
        _activePanels.Clear();
    }
}
