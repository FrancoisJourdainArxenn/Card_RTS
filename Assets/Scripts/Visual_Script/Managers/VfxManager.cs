using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class VfxManager : MonoBehaviour
{
    [SerializeField] private GameObject effectOverlay;
    private Image effectOverlayImage;

    void Awake()
    {
        if (effectOverlay != null)
            effectOverlayImage = effectOverlay.GetComponent<Image>();
    }

    public void Play(EffectVisualData data, int amount)
    {
        if (data == null) return;

        ZoneManager zone = GetComponentInParent<ZoneManager>();
        bool isVisible = zone == null || FogOfWarManager.Instance == null || !FogOfWarManager.Instance.IsZoneFogged(zone);

        if (isVisible)
        {
            if (data.vfxPrefab != null)
            {
                GameObject vfx = Instantiate(data.vfxPrefab, transform.position, Quaternion.identity);
                Destroy(vfx, data.vfxLifetime);
            }

            if (data.showAmount)
                VisualFeedbackEffect.CreateTextEffect(transform.position, amount, data.textColor);
        }

        if (effectOverlay != null && data.overlayDuration > 0)
        {
            if (effectOverlayImage != null && data.overlayMaterial != null)
                effectOverlayImage.material = data.overlayMaterial;
            StartCoroutine(ShowOverlay(data.overlayDuration));
        }
    }

    private IEnumerator ShowOverlay(float duration)
    {
        effectOverlay.SetActive(true);
        yield return new WaitForSeconds(duration);
        effectOverlay.SetActive(false);
    }
}
