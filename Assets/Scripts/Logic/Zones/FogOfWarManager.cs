using UnityEngine;
using System.Collections.Generic;

/// MULTIPLAYER NOTE: ObservingPlayer currently mirrors GlobalSettings.localPlayer
/// (toggled with Space for debug). When real multiplayer arrives, replace
/// ObservingPlayer with the local client's player from the network layer —
/// the rest of this class does not need to change.
/// </summary>
public class FogOfWarManager : MonoBehaviour
{
    public static FogOfWarManager Instance;
    public static readonly HashSet<int> ForceRevealedZones = new HashSet<int>();
    [SerializeField] private bool debugDisableFog = false;
    private Dictionary<int, bool> zoneFogCache = new Dictionary<int, bool>();
    private ZoneManager[] cachedZones;
    private bool _refreshPending;
    private Player _lastObserver;


    void Awake()
    {
        Instance = this;
    }

    void OnValidate()
    {
        if (Application.isPlaying)
            Refresh();
    }

    void Start()
    {
        cachedZones = FindObjectsByType<ZoneManager>(FindObjectsSortMode.None);
        UpdateAllZones();
    }

    // LateUpdate exécute l'update fog une seule fois par frame, même si Refresh() a été appelé plusieurs fois.
    void LateUpdate()
    {
        if (!_refreshPending) return;
        _refreshPending = false;
        UpdateAllZones();
    }

    // The player whose perspective we are currently showing.
    private Player ObservingPlayer => GlobalSettings.Instance.localPlayer;

    // Static shortcut so any class can call FogOfWarManager.Refresh()
    // without needing a reference to the Instance.
    // Plusieurs appels dans le même frame ne déclenchent qu'un seul UpdateAllZones() via LateUpdate.
    public static void Refresh()
    {
        if (Instance != null)
            Instance._refreshPending = true;
    }

    // Recalculate fog for every neutral zone.
    public void UpdateAllZones()
    {
        if (GlobalSettings.Instance == null) return;

        Player observer = ObservingPlayer;
        bool observerChanged = observer != _lastObserver;
        _lastObserver = observer;

        ZoneManager[] allZones = cachedZones ?? FindObjectsByType<ZoneManager>(FindObjectsSortMode.None);
        foreach (ZoneManager zone in allZones)
        {
            // A neutral zone also has a NeutralZoneController for base fog.
            // Main base zones won't have one — that's fine, nbc will just be null.
            NeutralZoneController nbc = zone.GetComponent<NeutralZoneController>();
            UpdateZone(zone, nbc, observerChanged);
        }
    }

    // Recalculate and apply fog for a single zone.
    void UpdateZone(ZoneManager zone, NeutralZoneController nbc, bool observerChanged)
    {
        Player observer = ObservingPlayer;
        if (observer == null) return;

        Player enemy = observer.otherPlayer;
        AreaPosition observerAreaPos = GetAreaPosition(observer);
        AreaPosition enemyAreaPos = GetAreaPosition(enemy);

        bool observerHasPresence = HasPresenceInZone(observer, zone, nbc)
            || ForceRevealedZones.Contains(zone.Logic.ID)
            || debugDisableFog;
        bool wasFogged = zoneFogCache.TryGetValue(zone.Logic.ID, out bool cached) ? cached : true;
        bool isFogged  = !observerHasPresence;
        bool stateChanged = wasFogged != isFogged;
        zoneFogCache[zone.Logic.ID] = isFogged;

        FogMapOverlay overlay = FogMapOverlay.Instance;
        if (overlay != null)
        {
            if (stateChanged)
            {
                Vector3 originPos = GetOriginPosForZone(zone, observer);
                if (isFogged) overlay.CoverZoneAnimated(zone, originPos);
                else          overlay.RevealZoneAnimated(zone, originPos);
            }
            // Pas de SetZoneFoggedInstant si l'état n'a pas changé : la texture est déjà correcte.
        }

        if (nbc != null)
        {
            nbc.SetEnemyBasesFogged(enemy, !observerHasPresence);
            nbc.SetPlayerBasesVisible(observer); // Always show observer's own captured bases
            nbc.ApplyColorForObserver(observer, observerHasPresence);
            nbc.UpdateNeutralBaseVisualFog(observerHasPresence);
        }

        // Ne mettre à jour les visuels que si l'état du fog ou l'observateur a changé.
        // Évite des SetActive et Image.color inutiles (Canvas rebuild, GPU resource churn).
        if (!stateChanged && !observerChanged)
            return;

        foreach (PlayerArea pa in zone.subZones)
        {
            if (pa.tableVisual == null) continue;

            if (pa.owner == observerAreaPos)
            {
                // The observer always sees their own board.
                pa.tableVisual.SetFogged(false);
            }
            else if (pa.owner == enemyAreaPos)
            {
                // Enemy board is visible only if observer has presence here.
                pa.tableVisual.SetFogged(!observerHasPresence);
            }
        }

        // --- Bases joueur (fog comme les bases neutres) ---
        // L'observer voit toujours sa propre base (ApplyFogForObserver met à jour le texte PV) —
        // sauf si le bâtiment n'est pas actif (voir Player.HomeBuildingActive) : il n'existe plus comme
        // objet de jeu (voir Player.SpawnHomeUnitIfConfigured, qui le désactive), donc le fog ne doit
        // jamais le réafficher — sans ce garde, ApplyFogForObserver(true) le SetActive(true)
        // inconditionnellement dès le premier recalcul de fog qui suit.
        if (observer.MainPArea != null
            && observer.MainPArea.parentZone == zone
            && observer.baseVisual != null
            && observer.HomeBuildingActive)
        {
            observer.baseVisual.ApplyFogForObserver(true);
        }

        // La base ennemie suit le fog : invisible jusqu'à être vue, puis dernier état connu
        if (enemy != null
            && enemy.MainPArea != null
            && enemy.MainPArea.parentZone == zone
            && enemy.baseVisual != null
            && enemy.HomeBuildingActive)
        {
            enemy.baseVisual.ApplyFogForObserver(observerHasPresence);
        }
    }
    public bool IsZoneFogged(ZoneManager zone)
    {
        if (debugDisableFog) return false;
        if (zone == null) return false;
        return !zoneFogCache.TryGetValue(zone.Logic.ID, out bool isFogged) || isFogged;
    }

    // Returns true if 'player' has at least one creature OR base in the given zone.

    bool HasPresenceInZone(Player player, ZoneManager zone, NeutralZoneController nbc)
    {
        // Bâtiment actif (voir Player.HomeBuildingActive) : toujours présent dans sa zone d'origine.
        // Les unités-bases, elles, donnent la présence comme n'importe quelle autre créature (voir la
        // boucle playedCards.Creatures ci-dessous, même principe que Player.CanPlayCreatureInArea) —
        // sans bâtiment actif, MainPArea ne garantit plus la vision.
        if (player.HomeBuildingActive && player.MainPArea.parentZone == zone)
        {
            return true;
        }

        // Find which of this player's areas is inside this zone.
        PlayerArea playerAreaInZone = null;
        
        foreach (PlayerArea pa in player.PAreas)
        {
            if (pa.parentZone == zone)
            {
                playerAreaInZone = pa;
                break;
            }
        }

        if (playerAreaInZone == null) return false;

        // Check creatures.
        foreach (CreatureLogic c in player.playedCards.Creatures)
        {
            if (c.BaseID == playerAreaInZone.baseID)
                return true;
        }

        // Check bases (neutral zones only).
        if (nbc != null)
        {
            foreach (var kvp in BaseLogic.BasesCreatedThisGame)
            {
                BaseLogic b = kvp.Value;
                if (b.owner == player && b.neutralBaseController == nbc)
                    return true;
            }
        }

        return false;
    }

    AreaPosition GetAreaPosition(Player player)
    {
        return player == GlobalSettings.Instance.LowPlayer ? AreaPosition.Low : AreaPosition.Top;
    }

    private Vector3 GetOriginPosForZone(ZoneManager zone, Player player)
    {
        foreach (PlayerArea pa in zone.subZones)
        {
            if (pa.owner == GetAreaPosition(player))
                return pa.BasePosition != null
                    ? pa.BasePosition.position
                    : pa.transform.position;
        }
        return zone.transform.position;
    }

}
