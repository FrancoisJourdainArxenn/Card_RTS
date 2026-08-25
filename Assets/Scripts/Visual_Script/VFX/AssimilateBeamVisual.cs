using UnityEngine;
using System.Collections;

// À poser sur la racine du prefab Vfx_Assimilation. Cone Renderer/Cone Transform doivent pointer
// explicitement vers Assimilation_Cone (pas de GetComponentInChildren : la hiérarchie contient
// aussi le ParticleSystemRenderer de la Spirale, qui ne doit ni être scalé ni recevoir _Progress).
public class AssimilateBeamVisual : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private Renderer coneRenderer;
    [SerializeField] private Transform coneTransform;

    [Header("Timing")]
    [Tooltip("Durée du balayage en secondes (plus petit = plus rapide).")]
    [SerializeField] private float progressDuration = 0.45f;

    [Header("Largeur")]
    [Tooltip("Largeur de la bande lumineuse qui suit le balayage (_EdgeWidth).")]
    [SerializeField] private float edgeWidth = 0.12f;

    [Header("Ondulation du bord")]
    [SerializeField] private float waveAmount = 0.04f;
    [SerializeField] private float wavePanSpeed = 0.3f;

    [Header("Turbulence interne")]
    [SerializeField] private float turbPanSpeed = 0.6f;
    [SerializeField] private float turbMin = 0.55f;

    // Mesh d'Assimilation_Cone = Plane primitif de Unity (fileID 10209) : 10x10 unités monde à
    // scale 1, centré sur son origine (s'étend de -5 à +5 en Z local). D'où la division par 10
    // pour convertir une distance monde en scale Z, et le décalage de -moitié pour que le bord
    // proche (au lieu du centre) colle au pivot — le pivot est sur la target (voir
    // PlayAssimilateVFXCommand), donc le mesh doit s'étendre uniquement vers la source, pas des
    // deux côtés du pivot comme le ferait un simple scale sur un mesh centré.
    private const float PlaneMeshSize = 10f;

    private Material materialInstance;

    public void Play(float coneLength)
    {
        coneTransform.localScale = new Vector3(coneTransform.localScale.x, coneTransform.localScale.y, coneLength / PlaneMeshSize);
        coneTransform.localPosition = new Vector3(coneTransform.localPosition.x, coneTransform.localPosition.y, -coneLength * 0.5f);

        materialInstance = coneRenderer.material;
        materialInstance.SetFloat("_EdgeWidth", edgeWidth);
        materialInstance.SetFloat("_WaveAmount", waveAmount);
        materialInstance.SetFloat("_WavePanSpeed", wavePanSpeed);
        materialInstance.SetFloat("_TurbPanSpeed", turbPanSpeed);
        materialInstance.SetFloat("_TurbMin", turbMin);

        StartCoroutine(PlayConsumeVFX());
    }

    private IEnumerator PlayConsumeVFX()
    {
        float t = 0f;
        while (t < progressDuration)
        {
            t += Time.deltaTime;
            materialInstance.SetFloat("_Progress", Mathf.Clamp01(t / progressDuration));
            yield return null;
        }

        Destroy(gameObject);
    }
}
