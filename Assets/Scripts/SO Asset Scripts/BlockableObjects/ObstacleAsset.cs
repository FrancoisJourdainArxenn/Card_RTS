using UnityEngine;

[CreateAssetMenu(menuName = "Obstacle Asset")]
public class ObstacleAsset : ScriptableObject
{
    public enum BlockerType { Destructible, Timed }

    public BlockerType type;
    public int maxHealth;   // utilisé si type == Destructible
    public int duration;    // utilisé si type == Timed (en tours)
}
