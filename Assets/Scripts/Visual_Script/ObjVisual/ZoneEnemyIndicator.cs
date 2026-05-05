using UnityEngine;

public class ZoneEnemyIndicator : MonoBehaviour
{
    public GameObject indicatorObject;

    private ZoneVisual _zone;

    public static event System.Action OnRefreshRequested;

    void Awake()
    {
        _zone = GetComponent<ZoneVisual>();
        if (indicatorObject != null)
            indicatorObject.SetActive(false);
    }

    void OnEnable()  => OnRefreshRequested += Refresh;
    void OnDisable() => OnRefreshRequested -= Refresh;

    public static void RefreshAll() => OnRefreshRequested?.Invoke();

    private void Refresh()
    {
        if (indicatorObject == null || GlobalSettings.Instance == null) return;
        Player local = GlobalSettings.Instance.localPlayer;
        if (local == null) return;
        Player enemy = local.otherPlayer;
        if (enemy == null) return;

        bool show = EnemyIsInThisZone(enemy) && LocalIsInAdjacentZone(local);
        indicatorObject.SetActive(show);
    }

    private bool EnemyIsInThisZone(Player enemy)
    {
        foreach (CreatureLogic c in enemy.playedCards.Creatures)
        {
            ZoneLogic zone = c.Zone;
            if (zone != null && zone == _zone.Logic)
                return true;
        }
        return false;
    }

    private bool LocalIsInAdjacentZone(Player local)
    {
        foreach (CreatureLogic c in local.playedCards.Creatures)
        {
            ZoneLogic zone = c.Zone;
            if (zone != null && _zone.Logic.IsAdjacentTo(zone))
                return true;
        }
        return false;
    }
}
