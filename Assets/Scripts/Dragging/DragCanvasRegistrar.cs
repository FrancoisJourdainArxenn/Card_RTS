using UnityEngine;

public class DragCanvasRegistrar : MonoBehaviour
{
    void Awake()
    {
        DragCreatureActions.SetDragCanvas(GetComponent<Canvas>());
    }
}
