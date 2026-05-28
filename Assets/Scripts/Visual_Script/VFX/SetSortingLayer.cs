using UnityEngine;

public class SetSortingLayer : MonoBehaviour
{
    [SerializeField] private string layerName = "VFX";
    [SerializeField] private int order = 10;

    private void Awake()
    {
        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sortingLayerName = layerName;
            rend.sortingOrder = order;
        }
    }
}
