using System.Collections.Generic;
using UnityEngine;

// Doit s'enregistrer avant GlobalSettings.Awake() (ordre par défaut) pour que
// GlobalSettings.InitFromMap() puisse inclure ces zones dans Player.PAreas.
// Même mécanisme que MapManager ([DefaultExecutionOrder(-200)]) mais plus tôt.
[DefaultExecutionOrder(-300)]
public class CrossingZoneSlot : MonoBehaviour
{
    private static readonly List<CrossingZoneSlot> allSlots = new List<CrossingZoneSlot>();
    public static IReadOnlyList<CrossingZoneSlot> AllSlots => allSlots;

    public ZoneManager Zone { get; private set; }
    public ZoneCombatResolver Resolver { get; private set; }
    public bool InUse { get; private set; }

    // Référence libre (pas un enfant) : cette slot est isolée dans un coin de la scène,
    // l'icône doit rester un objet indépendant repositionné sur la map au moment d'être montrée.
    [SerializeField] SpriteRenderer pathIcon;

    // Par joueur impliqué dans le croisement en cours : d'où il venait et où il allait
    // à l'origine, pour pouvoir dispatcher les survivants une fois le combat résolu.
    private readonly Dictionary<Player, (int originBaseID, int intendedTargetBaseID)> memory = new();

    void Awake()
    {
        Zone = GetComponent<ZoneManager>();
        Resolver = GetComponent<ZoneCombatResolver>();
        allSlots.Add(this);

        if (Zone == null)
            Debug.LogError($"[CrossingZoneSlot] {name} n'a pas de ZoneManager/ZoneVisual sur le même GameObject.", this);
        if (Resolver == null)
            Debug.LogError($"[CrossingZoneSlot] {name} n'a pas de ZoneCombatResolver sur le même GameObject.", this);

        // L'icône ne doit être visible que pendant un Encounter en cours ; état de départ
        // masqué quel que soit ce qui a été laissé activé dans l'Inspector.
        HidePathIcon();
    }

    void OnDestroy()
    {
        allSlots.Remove(this);
    }

    public static CrossingZoneSlot GetFreeSlot()
    {
        foreach (CrossingZoneSlot slot in allSlots)
            if (!slot.InUse)
                return slot;
        return null;
    }

    public void MarkInUse() => InUse = true;

    public void MarkFree()
    {
        InUse = false;
        memory.Clear();
    }

    public void RecordOriginAndDestination(Player player, int originBaseID, int intendedTargetBaseID)
        => memory[player] = (originBaseID, intendedTargetBaseID);

    public bool TryGetMemory(Player player, out int originBaseID, out int intendedTargetBaseID)
    {
        if (memory.TryGetValue(player, out var entry))
        {
            originBaseID = entry.originBaseID;
            intendedTargetBaseID = entry.intendedTargetBaseID;
            return true;
        }
        originBaseID = -1;
        intendedTargetBaseID = -1;
        return false;
    }

    // Point milieu et direction horizontale (XZ) du trajet origine -> destination d'un des
    // joueurs impliqués dans ce croisement, pour donner à la caméra un vrai point de mire sur
    // le chemin (distinct du cadrage serré de l'ancre de combat) plutôt qu'une direction arbitraire.
    public bool TryGetCrossingPath(out Vector3 midpoint, out Vector3 direction)
    {
        foreach (var kvp in memory)
        {
            Player player = kvp.Key;
            PlayerArea originArea = player.GetPlayerAreaByID(kvp.Value.originBaseID);
            PlayerArea targetArea = player.GetPlayerAreaByID(kvp.Value.intendedTargetBaseID);
            if (originArea == null || targetArea == null)
                continue;

            Vector3 delta = targetArea.transform.position - originArea.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.0001f)
            {
                midpoint = (originArea.transform.position + targetArea.transform.position) * 0.5f;
                direction = delta.normalized;
                return true;
            }
        }
        midpoint = Vector3.zero;
        direction = Vector3.zero;
        return false;
    }

    public void ShowPathIcon()
    {
        if (pathIcon == null)
            return;
        if (TryGetCrossingPath(out Vector3 midpoint, out _))
        {
            pathIcon.transform.position = midpoint;
            pathIcon.enabled = true;
        }
    }

    public void HidePathIcon()
    {
        if (pathIcon != null)
            pathIcon.enabled = false;
    }
}
