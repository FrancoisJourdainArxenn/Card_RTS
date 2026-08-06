using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ReminderTextManager : MonoBehaviour
{
    public static ReminderTextManager Instance;

    public GameObject keywordPanelPrefab;
    public Transform tooltipContainer;
    public float tooltipDelay = 1f;
    public float fadeInDuration = 0.3f;
    public float fadeOutDuration = 0.15f;
    [SerializeField] private Vector2 tooltipOffset = new(320f, 0f);

    public RectTransform TooltipRect => (RectTransform)tooltipContainer;
    public Vector2 TooltipOffset => tooltipOffset;

    private CanvasGroup canvasGroup;
    private readonly List<GameObject> _activePanels = new();

    void Awake()
    {
        Instance = this;
        canvasGroup = tooltipContainer.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0;
    }

    // Construit les panneaux (donc l'encombrement réel du bloc de tooltips) sans encore le positionner,
    // pour que l'appelant puisse mesurer sa taille avant de fixer la position finale de la carte.
    public void BuildTooltips(List<Keyword> keywords)
    {
        canvasGroup.DOKill();
        ClearPanels();

        if (keywords == null || keywords.Count == 0) return;

        foreach (var keyword in keywords)
        {
            if (keyword == null) continue;
            var panel = Instantiate(keywordPanelPrefab, tooltipContainer);
            panel.GetComponent<KeywordTooltipPanel>().Setup(keyword);
            _activePanels.Add(panel);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(TooltipRect);
    }

    // Ancre le bloc de tooltips au bord droit de la carte, à sa position finale (post-clamp),
    // pour qu'il reste synchronisé même quand la carte a été repositionnée pour rester à l'écran.
    public void AnchorToCard(Vector2 cardAnchoredPosition)
    {
        TooltipRect.anchoredPosition = cardAnchoredPosition + tooltipOffset;
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
        foreach (var p in _activePanels)
            Destroy(p);
        _activePanels.Clear();
    }
}
