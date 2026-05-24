using UnityEngine;

public class DebugCenteredSlots : CenteredSlots
{
    public float HoverRadiusPx = 30f;
    [Range(0f, 1f)] public float Alpha = 0.5f;
    public Transform[] Children;

    void Update()
    {
        if (Children == null || Children.Length == 0) return;

        int count = Children.Length;
        Vector2 mouseScreen = Input.mousePosition;

        for (int i = 0; i < count; i++)
        {
            var child = Children[i];
            if (child == null) continue;

            child.position = GetSlotPosition(i, count);

            var r = child.GetComponentInChildren<Renderer>(true);
            if (r == null) continue;
#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(r)) continue;
#endif
            Vector2 childScreen = Camera.main.WorldToScreenPoint(child.position);
            float dist = Vector2.Distance(mouseScreen, childScreen);
            Color c = dist < HoverRadiusPx ? Color.red : Color.black;
            c.a = Alpha;
            r.material.SetColor("_BaseColor", c);
        }
    }
}
