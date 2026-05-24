using UnityEngine;

public class CenteredSlots : MonoBehaviour
{
    [Min(0f)] public float Spacing = 2f;
    [Min(1)]  public int   MaxCreatures = 7;

    public Vector3 GetSlotPosition(int slotIndex, int totalCount)
    {
        float offset = (slotIndex - (totalCount - 1) / 2f) * Spacing;
        return transform.position + Vector3.right * offset;
    }
}
