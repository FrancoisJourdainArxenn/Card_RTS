using UnityEngine;

public class CenteredSlots : MonoBehaviour
{
    [Min(0f)] public float Spacing = 2f;

    public Vector3 GetSlotPosition(int slotIndex, int totalCount)
    {
        float offset = (slotIndex - (totalCount - 1) / 2f) * Spacing;
        return transform.position + Vector3.right * offset;
    }
}
