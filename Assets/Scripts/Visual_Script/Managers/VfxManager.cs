using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;

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

    public void PlayProjectile(EffectVisualData data, int amount, Vector3 origin, System.Action onImpact)
    {
        if (data == null || data.vfxPrefab == null) { onImpact?.Invoke(); return; }

        ZoneManager zone = GetComponentInParent<ZoneManager>();
        bool isVisible = zone == null || FogOfWarManager.Instance == null || !FogOfWarManager.Instance.IsZoneFogged(zone);
        if (!isVisible) { onImpact?.Invoke(); return; }

        Vector3 destination = transform.position;
        float speed = VisualManager.Instance != null ? VisualManager.Instance.ProjectileSpeed : 18f;
        float minDur = VisualManager.Instance != null ? VisualManager.Instance.ProjectileMinDuration : 0.12f;
        float distance = Vector3.Distance(origin, destination);
        float duration = Mathf.Max(minDur, speed > 0.01f ? distance / speed : minDur);

        GameObject vfx = Instantiate(data.vfxPrefab, origin, Quaternion.identity);
        RangedProjectile projScript = vfx.AddComponent<RangedProjectile>();
        projScript.Play(origin, destination, duration, () =>
        {
            var config = vfx.GetComponent<VfxAmountConfig>();
            if (config != null)
                VisualFeedbackEffect.CreateTextEffect(destination, amount, config.textColor, config.prefix);
            onImpact?.Invoke();
        });
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

    private GameObject shieldVfxInstance;
    // Valeur actuellement affichée sur la bulle de bouclier — maintenue par AddShieldVfx/ConsumeShieldVfx
    // (voir ces méthodes) pour rejouer les gains/consommations un par un, DANS L'ORDRE OÙ LES COMMANDES
    // VISUELLES S'EXÉCUTENT RÉELLEMENT, plutôt que de rejouer un total absolu capturé côté logique (qui
    // peut être bien plus avancé — toute la planification d'un round de combat tourne avant que la
    // moindre commande visuelle ne joue) — voir ApplyShieldCommand/ConsumeShieldCommand.
    private int _shieldAmount;

    public void ShowShieldVfx(GameObject prefab, int amount)
    {
        if (prefab == null) return;
        HideShieldVfx();
        shieldVfxInstance = Instantiate(prefab, transform);
        _shieldAmount = amount;
        SetShieldText(_shieldAmount);
    }

    // Ajoute `delta` à la valeur actuellement AFFICHÉE (pas à ShieldValue, déjà à jour côté logique) —
    // crée la bulle si elle n'existe pas encore (premier gain de la créature). Utilisé par
    // ApplyShieldCommand pour rejouer un gain de bouclier "coup par coup", cohérent avec ce qu'un
    // ConsumeShieldVfx précédent a déjà affiché, plutôt que d'écraser avec un total capturé côté
    // logique bien plus tôt (voir commentaire sur _shieldAmount).
    public void AddShieldVfx(GameObject prefab, int delta)
    {
        if (shieldVfxInstance == null)
        {
            ShowShieldVfx(prefab, delta);
            return;
        }
        _shieldAmount += delta;
        SetShieldText(_shieldAmount);
    }

    // Soustrait `delta` (montant réellement absorbé par CE coup) à la valeur affichée — masque la
    // bulle si elle tombe à 0 ou moins. Utilisé par ConsumeShieldCommand ; voir AddShieldVfx pour le
    // raisonnement symétrique côté gain.
    public void ConsumeShieldVfx(int delta)
    {
        if (shieldVfxInstance == null) return;
        _shieldAmount -= delta;
        if (_shieldAmount <= 0)
            HideShieldVfx();
        else
            SetShieldText(_shieldAmount);
    }

    public void UpdateShieldVfx(int remainingAmount)
    {
        if (shieldVfxInstance == null) return;
        _shieldAmount = remainingAmount;
        SetShieldText(_shieldAmount);
    }

    private void SetShieldText(int amount)
    {
        if (shieldVfxInstance == null) return;
        var config = shieldVfxInstance.GetComponent<VfxAmountConfig>();
        var label = shieldVfxInstance.GetComponentInChildren<TMP_Text>();
        if (label != null && config != null)
        {
            label.color = config.textColor;
            label.text = config.prefix + amount;
        }
    }

    public void HideShieldVfx()
    {
        if (shieldVfxInstance != null)
        {
            Destroy(shieldVfxInstance);
            shieldVfxInstance = null;
        }
        _shieldAmount = 0;
    }

    public void ShowDeathPending()
    {
        if (deathPendingMaterial == null) return;
        effectOverlayImage.material = deathPendingMaterial;
        effectOverlay.SetActive(true);
    }

    // Retourne la durée du VFX joué (0 si rien n'a été joué), pour que l'appelant (CreatureDieCommand)
    // puisse retarder le re-order de la rangée sans retarder la disparition de la créature elle-même.
    public float PlayDeath()
    {
        if (deathVfxPrefab == null) return 0f;

        ZoneManager zone = GetComponentInParent<ZoneManager>();
        bool isVisible = zone == null || FogOfWarManager.Instance == null
                        || !FogOfWarManager.Instance.IsZoneFogged(zone);
        if (!isVisible) return 0f;

        GameObject vfx = Instantiate(deathVfxPrefab, transform.position, Quaternion.identity);
        float lifetime = GetParticleLifetime(vfx);
        // Debug.Log($"[DBG][Timing] PlayDeath {gameObject.name} lifetime={lifetime:F3} @ {Time.realtimeSinceStartup:F3}");
        Destroy(vfx, lifetime);
        return lifetime;
    }

    public void PlayTriggerVfx(GameObject prefab)
    {
        if (prefab == null) return;

        ZoneManager zone = GetComponentInParent<ZoneManager>();
        bool isVisible = zone == null || FogOfWarManager.Instance == null
                        || !FogOfWarManager.Instance.IsZoneFogged(zone);
        if (!isVisible) return;

        GameObject vfx = Instantiate(prefab, transform.position, Quaternion.identity);
        Destroy(vfx, GetParticleLifetime(vfx));
    }

    public void PlaySourceVfx(EffectVisualData data)
    {
        if (data == null || !data.onSource || data.vfxPrefabOnSource == null) return;
        PlayTriggerVfx(data.vfxPrefabOnSource);
    }
}
