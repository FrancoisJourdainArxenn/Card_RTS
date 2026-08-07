using UnityEngine;

public class ZoneEnemyIndicator : MonoBehaviour
{
    public GameObject indicatorObject;
    private bool _isActive = false;

    private ZoneManager _zone;

    public static event System.Action OnRefreshRequested;

    void Awake()
    {
        _zone = GetComponent<ZoneManager>();
        if (indicatorObject != null)
            indicatorObject.SetActive(false);
    }

    void Start()
    {
        Refresh();
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

        bool enemyPresent = EnemyIsInThisZone(enemy);

        indicatorObject.SetActive(enemyPresent && ScoutEnterSO.HasScoutAdjacentTo(_zone.Logic, local));
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
}
