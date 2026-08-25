using UnityEngine;

[System.Serializable]
public class EffectVisualData
{
    public GameObject vfxPrefab;
    public Material overlayMaterial;
    public float OverrideOverlayDuration = 0f;
    public bool travelFromSource = false;
    [Tooltip("Si coché, aucune animation n'est jouée (ni VFX custom, ni repli sur l'attaque de la source), même si vfxPrefab est vide.")]
    public bool suppressAttackFallback = false;
}
