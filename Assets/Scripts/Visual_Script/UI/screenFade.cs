using System;
using DG.Tweening;
using UnityEngine;

// Overlay plein écran (CanvasGroup) utilisé pour masquer les coupures de caméra
// (changement d'ancre BattleCam / Crossing Combat) derrière un fondu au noir.
public class ScreenFade : MonoBehaviour
{
    public static ScreenFade Instance { get; private set; }

    [SerializeField] CanvasGroup canvasGroup;
    [Tooltip("Durée du fondu vers le noir, avant que la caméra ne soit repositionnée.")]
    public float fadeOutDuration = 0.35f;
    [Tooltip("Durée du fondu depuis le noir, une fois la caméra en place.")]
    public float fadeInDuration = 0.35f;
    public Ease fadeEase = Ease.InOutSine;

    Tween _fadeTween;

    void Awake()
    {
        Instance = this;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
    }

    public void FadeOut(Action onComplete)
    {
        _fadeTween?.Kill();
        canvasGroup.blocksRaycasts = true;
        _fadeTween = canvasGroup.DOFade(1f, fadeOutDuration)
            .SetEase(fadeEase)
            .OnComplete(() => onComplete?.Invoke());
    }

    public void FadeIn(Action onComplete = null)
    {
        _fadeTween?.Kill();
        _fadeTween = canvasGroup.DOFade(0f, fadeInDuration)
            .SetEase(fadeEase)
            .OnComplete(() =>
            {
                canvasGroup.blocksRaycasts = false;
                onComplete?.Invoke();
            });
    }
}
