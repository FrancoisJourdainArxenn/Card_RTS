using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;

public class ReminderTextManager : MonoBehaviour
{
    public static ReminderTextManager Instance;

    public GameObject keywordPanelPrefab;
    public Transform tooltipContainer;
    public float tooltipDelay = 1f;
    public float fadeInDuration = 0.3f;
    public float fadeOutDuration = 0.15f;

    private CanvasGroup canvasGroup;
    private readonly List<GameObject> _activePanels = new();

    void Awake()
    {
        Instance = this;
        canvasGroup = tooltipContainer.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0;
    }

    public void ShowTooltips(List<Keyword> keywords)
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
