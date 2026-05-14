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
                var config = vfx.GetComponent<VfxAmountConfig>();
                if (config != null)
                    VisualFeedbackEffect.CreateTextEffect(transform.position, amount, config.textColor, config.prefix);
                Destroy(vfx, GetParticleLifetime(vfx));
            }
        }

        if (effectOverlay != null && data.overlayMaterial != null)
        {
            if (effectOverlayImage != null)
                effectOverlayImage.material = data.overlayMaterial;
            float duration = data.OverrideOverlayDuration != 0 ? data.OverrideOverlayDuration : 1.5f;
            StartCoroutine(ShowOverlay(duration));

        }
    }

    private float GetParticleLifetime(GameObject vfx)
    {
        var ps = vfx.GetComponentInChildren<ParticleSystem>();
        if (ps != null)
        {
            var main = ps.main;
            return main.duration + main.startLifetime.constantMax;
        }
        return 2f;
    }


    private IEnumerator ShowOverlay(float duration)
    {
        effectOverlay.SetActive(true);
        yield return new WaitForSeconds(duration);
        effectOverlay.SetActive(false);
    }
}
