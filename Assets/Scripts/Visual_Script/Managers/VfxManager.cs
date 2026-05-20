using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class VfxManager : MonoBehaviour
{
    [SerializeField] private GameObject effectOverlay;
    
    [Header("Death Pending")]
    [SerializeField] private Material deathPendingMaterial;
    [SerializeField] private GameObject deathVfxPrefab;
    private Image effectOverlayImage;

    void Awake()
    {
        if (effectOverlay != null)
            effectOverlayImage = effectOverlay.GetComponent<Image>();
    }

    public void Play(EffectVisualData data, int amount, Vector3 offset = default)
    {
        if (data == null) return;

        ZoneManager zone = GetComponentInParent<ZoneManager>();
        bool isVisible = zone == null || FogOfWarManager.Instance == null || !FogOfWarManager.Instance.IsZoneFogged(zone);

        if (isVisible)
        {
            if (data.vfxPrefab != null)
            {
                Vector3 position = transform.position + offset;
                GameObject vfx = Instantiate(data.vfxPrefab, position, Quaternion.identity);
                var config = vfx.GetComponent<VfxAmountConfig>();
                if (config != null)
                    VisualFeedbackEffect.CreateTextEffect(position, amount, config.textColor, config.prefix);
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

    public void PlaySecond(EffectVisualData data, int amount)
    {
        if (data == null || data.vfxPrefab == null) return;

        ZoneManager zone = GetComponentInParent<ZoneManager>();
        bool isVisible = zone == null || FogOfWarManager.Instance == null || !FogOfWarManager.Instance.IsZoneFogged(zone);

        if (!isVisible) return;

        GameObject vfx = Instantiate(data.vfxPrefab, transform.position, Quaternion.identity);
        var config = vfx.GetComponent<VfxAmountConfig>();
        if (config != null)
            VisualFeedbackEffect.CreateTextEffect(transform.position + config.secondTextOffset, amount, config.textColor, config.prefix);
        Destroy(vfx, GetParticleLifetime(vfx));
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

    public void ShowDeathPending()
    {
        if (deathPendingMaterial == null) return;
        effectOverlayImage.material = deathPendingMaterial;
        effectOverlay.SetActive(true);
    }

    public void PlayDeath()
    {
        if (deathVfxPrefab == null) return;

        ZoneManager zone = GetComponentInParent<ZoneManager>();
        bool isVisible = zone == null || FogOfWarManager.Instance == null
                        || !FogOfWarManager.Instance.IsZoneFogged(zone);
        if (!isVisible) return;

        GameObject vfx = Instantiate(deathVfxPrefab, transform.position, Quaternion.identity);
        Destroy(vfx, GetParticleLifetime(vfx));
    }
}
