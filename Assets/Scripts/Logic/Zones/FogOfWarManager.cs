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

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Apply the initial fog state once all other objects have finished Awake().
        UpdateAllZones();
    }

    // The player whose perspective we are currently showing.
    private Player ObservingPlayer => GlobalSettings.Instance.localPlayer;

    // Static shortcut so any class can call FogOfWarManager.Refresh()
    // without needing a reference to the Instance.
    public static void Refresh()
    {
        if (Instance != null)
            Instance.UpdateAllZones();
    }

    // Recalculate fog for every neutral zone.
    public void UpdateAllZones()
    {
        if (GlobalSettings.Instance == null) return;

        // Cover ALL zones, not just neutral ones.
        ZoneVisual[] allZones = FindObjectsByType<ZoneVisual>(FindObjectsSortMode.None);
        foreach (ZoneVisual zone in allZones)
        {
            // A neutral zone also has a NeutralZoneController for base fog.
            // Main base zones won't have one — that's fine, nbc will just be null.
            NeutralZoneController nbc = zone.GetComponent<NeutralZoneController>();
            UpdateZone(zone, nbc);
        }
    }

    // Recalculate and apply fog for a single zone.
    void UpdateZone(ZoneVisual zone, NeutralZoneController nbc)
    {
        Player observer = ObservingPlayer;
        if (observer == null) return;

        Player enemy = observer.otherPlayer;
        AreaPosition observerAreaPos = GetAreaPosition(observer);
        AreaPosition enemyAreaPos = GetAreaPosition(enemy);

        bool observerHasPresence = HasPresenceInZone(observer, zone, nbc);

        foreach (PlayerArea pa in zone.subZones)
        {
            if (pa.tableVisual == null) continue;

            if (pa.owner == observerAreaPos)
            {
                // The observer always sees their own board.
                pa.tableVisual.SetFogged(false);
                pa.SetStatsFogged(false);
            }
            else if (pa.owner == enemyAreaPos)
            {
                // Enemy board is visible only if observer has presence here.
                pa.tableVisual.SetFogged(!observerHasPresence);
                pa.SetStatsFogged(!observerHasPresence);
            }
        }

        if (nbc != null)
        {
            nbc.SetEnemyBasesFogged(enemy, !observerHasPresence);
            nbc.SetPlayerBasesVisible(observer); // Always show observer's own captured bases
            nbc.ApplyColorForObserver(observer, observerHasPresence);
            nbc.UpdateNeutralBaseVisualFog(observerHasPresence);
        }

        // Show/hide all build spots in the zone based on observer presence
        foreach (BuildSpotVisual spot in zone.GetComponentsInChildren<BuildSpotVisual>(true))
            spot.gameObject.SetActive(observerHasPresence);
       
       foreach (var kvp in BuildingLogic.BuildingsCreatedThisGame)
        {
            BuildingLogic b = kvp.Value;
            if (b.OriginSpot == null || b.OriginSpot.Zone != zone) continue;

            GameObject buildingGO = IDHolder.GetGameObjectWithID(b.UniqueBuildingID);
            if (buildingGO == null) continue;

            buildingGO.SetActive(b.owner == observer || observerHasPresence);
        }
        // --- Bases joueur (fog comme les bases neutres) ---
        // L'observer voit toujours sa propre base
        if (observer.MainPArea != null 
            && observer.MainPArea.parentZone == zone 
            && observer.baseVisual != null)
        {
            observer.baseVisual.gameObject.SetActive(true);
        }

        // La base ennemie suit le fog : invisible jusqu'à être vue, puis dernier état connu
        if (enemy != null 
            && enemy.MainPArea != null 
            && enemy.MainPArea.parentZone == zone 
            && enemy.baseVisual != null)
        {
            enemy.baseVisual.ApplyFogForObserver(observerHasPresence);
        }
    }
    // Returns true if 'player' has at least one creature OR base in the given zone.

    bool HasPresenceInZone(Player player, ZoneVisual zone, NeutralZoneController nbc)
    {
        if(player.MainPArea.parentZone == zone)
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

        foreach (BuildingLogic b in player.playedCards.Buildings)
        {
            if (b.OriginSpot != null && b.OriginSpot.Zone == zone)
                return true;
        }

        return false;
    }

    AreaPosition GetAreaPosition(Player player)
    {
        return player == GlobalSettings.Instance.LowPlayer ? AreaPosition.Low : AreaPosition.Top;
    }
}
