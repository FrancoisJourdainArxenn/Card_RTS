using UnityEngine;

[System.Serializable]
public class EffectVisualData
{
    public GameObject vfxPrefab;
    public Material overlayMaterial;
    public float overlayDuration = 1.5f;
    public float vfxLifetime = 1.5f;
    public float AttackDuration = 0.1f;


    public bool showAmount = true;
    public Color textColor = Color.white;


}
