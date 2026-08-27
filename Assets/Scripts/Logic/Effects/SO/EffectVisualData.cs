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

    [Header("Source Echo")]
    [Tooltip("Si coché, vfxPrefabOnSource est aussi joué sur la source de l'effet (en plus du VFX ci-dessus, qui n'est pas remplacé), pour repérer le déclenchement — utile pour un buff dont le VFX principal est sur d'autres unités.")]
    public bool onSource = false;
    public GameObject vfxPrefabOnSource;
}
