using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// Doit être posé sur un objet TOUJOURS actif (le Canvas racine), pour que Awake()
// s'exécute et que Instance soit prêt même si le panneau visuel est désactivé
// par défaut dans la scène.
public class ScreenFade : MonoBehaviour
{
    public static ScreenFade Instance { get; private set; }

    [Header("Panneau (désactivé par défaut dans la scène)")]
    [SerializeField] GameObject fadePanel;
    [SerializeField] Image fadeImage;
    [SerializeField] Material fadeMaterialTemplate;

    [Header("Timing")]
    public float fadeOutDuration = 0.6f;
    public float fadeInDuration = 0.6f;
    public Ease fadeEase = Ease.InOutSine;

    static readonly int ProgressID = Shader.PropertyToID("_Progress");

    Material _fadeMaterial;
    Tween _fadeTween;

    void Awake()
    {
        Instance = this;
        // Instance propre du material pour ne pas modifier l'asset partagé.
        _fadeMaterial = Instantiate(fadeMaterialTemplate);
        fadeImage.material = _fadeMaterial;
        _fadeMaterial.SetFloat(ProgressID, 0f);
    }

    public void FadeOut(Action onComplete)
    {
        _fadeTween?.Kill();
        fadePanel.SetActive(true);
        _fadeMaterial.SetFloat(ProgressID, 0f);
        _fadeTween = DOTween.To(
                () => _fadeMaterial.GetFloat(ProgressID),
                v => _fadeMaterial.SetFloat(ProgressID, v),
                1f, fadeOutDuration)
            .SetEase(fadeEase)
            .OnComplete(() => onComplete?.Invoke());
    }

    public void FadeIn(Action onComplete = null)
    {
        _fadeTween?.Kill();
        _fadeTween = DOTween.To(
                () => _fadeMaterial.GetFloat(ProgressID),
                v => _fadeMaterial.SetFloat(ProgressID, v),
                0f, fadeInDuration)
            .SetEase(fadeEase)
            .OnComplete(() =>
            {
                fadePanel.SetActive(false);
                onComplete?.Invoke();
            });
    }
}
