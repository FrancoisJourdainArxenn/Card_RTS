using UnityEngine;

public class VisualManager : MonoBehaviour
{
    public static VisualManager Instance;

    [Header("Colors")]
    public Color statBuffColor   = new Color(0.48584908f, 0.8990354f, 1f, 1f);
    public Color statDebuffColor = new Color(0.6039216f, 0.20803499f, 0.14117645f, 1f);

    [Header("Numbers and Values")]
    public float CardPreviewTime = 0.1f;
    public float CardTransitionTime = 0.1f;
    public float CardPreviewTimeFast = 0.05f;
    public float CardTransitionTimeFast = 0.05f;
    public float AttackMoveDuration = 0.4f;   // durée du mouvement aller-retour
    public float AttackPostDelay = 1f;       // pause après chaque attaque
    public float AttackWindupDuration = 0.5f; // durée de l'élan (recul + élévation) avant le coup
    public float AttackWindupBack = 0.01f;      // distance de recul pendant l'élan
    public float AttackWindupHeight = 4f;   // hauteur d'élévation pendant l'élan
    public float ProjectileSpeed = 7f;        // vitesse du projectile (unités/seconde) — la durée de vol dépend de la distance réelle à la cible
    public float ProjectileMinDuration = 0.5f; // durée de vol plancher, pour qu'un tir à courte distance ou un attaquant très rapide reste visible
    public float MeleeMultiTargetVfxLifetime = 1.5f; // délai avant destruction du Melee VFX (cibles secondaires), le temps qu'il finisse de se jouer
    public float RaiseEffectVisualDelay = 0.5f; // délai avant que RaiseEffectVisualCommand ne se termine
    public float RowReorderDuration = 0.3f; // durée du glissement des créatures vers leur nouveau slot après une mort

    [Header("Pop Animation")]
    public float popStrength = 2f;
    public float popDuration = 0.5f;

    [Header("Camera Shake (on damage dealt)")]
    [Tooltip("How long (seconds) before the visual impact the camera shake starts, so it reads as landing on the hit instead of after it.")]
    public float CameraShakeAnticipation = 0.05f;
    [Tooltip("Attack stat threshold (X) at or above which a unit dealing damage triggers a light camera shake.")]
    public int CameraShakeThresholdLight = 5;
    public float CameraShakeStrengthLight = 0.15f;
    public float CameraShakeDurationLight = 0.2f;
    [Tooltip("Attack stat threshold (Y) at or above which a unit dealing damage triggers a strong camera shake.")]
    public int CameraShakeThresholdStrong = 10;
    public float CameraShakeStrengthStrong = 0.35f;
    public float CameraShakeDurationStrong = 0.3f;

    [Header("Card Tier Icons")]
    public Sprite[] CardTierIcons = new Sprite[5]; // index 0 = T1, 1 = T2, 2 = T3, 3 = T4, 4 = T5

    void Awake()
    {
        Instance = this;
    }
}
