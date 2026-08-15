using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine.UI;

public class TableVisual : MonoBehaviour
{
    public AreaPosition owner;
    public CenteredSlots rangedSlots;
    public CenteredSlots meleeSlots;
    public SameDistanceChildren pendingSlots;
    public GameObject glow;
    public Color ownerColor;
    [SerializeField] public LayerMask tableRaycastMask;
    [SerializeField] public List<GameObject> MeleeCreaturesOnTable  = new List<GameObject>();
    [SerializeField] public List<GameObject> RangedCreaturesOnTable = new List<GameObject>();
    [SerializeField] public List<GameObject> PendingCreaturesOnTable = new List<GameObject>();
    [HideInInspector] public PlayerArea ownerArea;

    //#Ghost versions when losing visions
    private bool hasBeenSeen = false;
    private List<GameObject> meleeGhostCreatures = new List<GameObject>();
    private List<GameObject> rangedGhostCreatures = new List<GameObject>();


    private bool cursorOverThisTable = false;
    private bool isFogged = false;
    private BoxCollider col;
    private int _previewIndex = -1;
    private bool _previewIsMelee;
    private GameObject _movingCreature;
    // Créatures dont le déplacement vers une autre zone est en attente (résolution en fin de phase Command) :
    // affichées sur des slots fixes au-delà de la capacité max de la rangée (voir PlaceRowOnSlots), sans que
    // leur position réelle dans MeleeCreaturesOnTable/RangedCreaturesOnTable ne soit modifiée, pour qu'une
    // annulation les remette instantanément en place. Liste (pas HashSet) pour garder un ordre d'attente stable.
    private readonly List<GameObject> _pendingRowEndCreatures = new List<GameObject>();

    // Relayout(s) reçus pendant qu'une attaque était en vol (voir CreatureAttackVisual.AnyAttackInFlight) :
    // rejoués une seule fois, avec la position à jour de toutes les créatures, dès que la dernière
    // attaque en vol se termine — pour ne jamais faire glisser une cible sous une charge/un projectile
    // déjà lancé vers sa position figée.
    private bool _relayoutPending = false;
    private readonly List<System.Action> _pendingRelayoutCallbacks = new List<System.Action>();

    // Dernier ordre connu (IDs permanents, voir PermanentIDOf) pour chaque rangée, reçu via
    // ApplyCreatureOrder. Réappliqué idempotemment à chaque arrivée d'une créature en attente
    // (voir MoveCreatureToIndex) : la position finale d'une créature ne dépend jamais d'un index
    // calculé de façon incrémentale, seulement de ce tri.
    private int[] _lastKnownMeleeOrder = System.Array.Empty<int>();
    private int[] _lastKnownRangedOrder = System.Array.Empty<int>();
    public IReadOnlyList<int> LastKnownMeleeOrder => _lastKnownMeleeOrder;
    public IReadOnlyList<int> LastKnownRangedOrder => _lastKnownRangedOrder;

    // list[0] = leftmost = attaque en premier
    public IEnumerable<GameObject> AllCreaturesOnTable =>
        MeleeCreaturesOnTable.Concat(RangedCreaturesOnTable);
    // Nombre de créatures occupant réellement la rangée, en ignorant celles en pending move
    // (déplacement en attente de résolution en fin de phase Command).
    public int EffectiveRowCount(bool isMelee)
    {
        List<GameObject> row = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;
        return row.Count(go => go != null
            && !go.GetComponent<OneCreatureManager>().HasPendingMove);
    }

    public bool RowHasSpace(bool isMelee) =>
        EffectiveRowCount(isMelee) < GlobalSettings.Instance.MaxCreaturePerRow;


    public static bool CursorOverSomeTable
    {
        get
        {
            foreach (TableVisual t in GameObject.FindObjectsByType<TableVisual>(FindObjectsSortMode.None))
                if (t.CursorOverThisTable) return true;
            return false;
        }
    }
    public bool CursorOverThisTable => cursorOverThisTable;

    void Awake()
    {
        col = GetComponent<BoxCollider>();
    }

    void OnEnable()
    {
        CreatureAttackVisual.OnAllAttacksResolved += FlushPendingRelayout;
    }

    void OnDisable()
    {
        CreatureAttackVisual.OnAllAttacksResolved -= FlushPendingRelayout;
    }

    public void RefreshSlotsPositions() => PlaceCreaturesOnNewSlots();

    private static readonly RaycastHit[] _raycastBuffer = new RaycastHit[8];
    void Update()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int count = Physics.RaycastNonAlloc(ray, _raycastBuffer, 300f, tableRaycastMask, QueryTriggerInteraction.Ignore);
        cursorOverThisTable = false;
        for (int i = 0; i < count; i++)
            if (_raycastBuffer[i].collider == col) { cursorOverThisTable = true; break; }
    }

    public void SetHighlight(bool active)
    {
        if (BuildingShopVisual.IsOpen) active = false;
        if (glow == null) return;
        glow.GetComponent<Image>().color = ownerColor;
        glow.SetActive(active);
    }

    public void SetFogged(bool fogged)
    {
        isFogged = fogged;

        if(!fogged)
        {
            hasBeenSeen = true;
            DestroyGhosts();
            foreach (GameObject c in MeleeCreaturesOnTable)  if (c != null) c.SetActive(true);
            foreach (GameObject c in RangedCreaturesOnTable) if (c != null) c.SetActive(true);    
        }
        else
        {
            if(hasBeenSeen)
                CreateGhosts();
            foreach (GameObject c in MeleeCreaturesOnTable)  if (c != null) c.SetActive(false);
            foreach (GameObject c in RangedCreaturesOnTable) if (c != null) c.SetActive(false);    
        }
        
    }

    // rowLocalPos : 0 = le plus à gauche dans la rangée
    public void AddCreatureAtIndex(CardAsset ca, int UniqueID, int rowLocalPos, int baseID, bool completeCommand = true, int? overrideAttack = null, int? overrideHealth = null)
    {
        bool isMelee = ca.melee;
        CenteredSlots rowSlots      = GetRowSlots(isMelee);
        List<GameObject> targetList = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;

        int listIndex = Mathf.Min(rowLocalPos, targetList.Count);
        int newCount  = targetList.Count + 1;
        Vector3 spawnPos = rowSlots.GetSlotPosition(listIndex, newCount);

        GameObject creature = CreateCreatureGO(ca, UniqueID, baseID, spawnPos, overrideAttack, overrideHealth);
        creature.transform.SetParent(rowSlots.transform);
        targetList.Insert(listIndex, creature);

        WhereIsTheCardOrCreature w = creature.GetComponent<WhereIsTheCardOrCreature>();
        w.Slot = rowLocalPos;
        w.VisualState = owner == AreaPosition.Low ? VisualStates.LowTable : VisualStates.TopTable;

        if (isFogged) creature.SetActive(false);
        if (completeCommand)
            PlaceCreaturesOnNewSlots(Command.CommandExecutionComplete);
        else
            PlaceCreaturesOnNewSlots();
    }

    // rowLocalPos : 0 = le plus à gauche dans la rangée
    public void MoveCreatureToIndex(GameObject creature, int UniqueID, int rowLocalPos, int baseID)
    {
        bool isMelee = CreatureLogic.CreaturesCreatedThisGame.TryGetValue(UniqueID, out CreatureLogic cl) && cl.IsMelee;
        CenteredSlots rowSlots  = GetRowSlots(isMelee);
        List<GameObject> targetList = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;

        if (rowSlots == null)
        {
            Debug.LogError($"[TableVisual] MoveCreatureToIndex: rowSlots est null (isMelee={isMelee}). Vérifie que rangedSlots/meleeSlots sont assignés dans l'Inspecteur sur {gameObject.name}.");
            Command.CommandExecutionComplete();
            return;
        }
        creature.transform.SetParent(rowSlots.transform);
        // Position de repli seulement : SortListByIDs ci-dessous, basé sur le dernier ordre connu de
        // la rangée (voir ApplyCreatureOrder), est ce qui détermine réellement la position finale —
        // idempotent, jamais un index recalculé de façon incrémentale (voir DragCreatureActions.BroadcastRowOrder).
        targetList.Insert(Mathf.Min(rowLocalPos, targetList.Count), creature);
        SortListByIDs(targetList, isMelee ? _lastKnownMeleeOrder : _lastKnownRangedOrder);

        WhereIsTheCardOrCreature w = creature.GetComponent<WhereIsTheCardOrCreature>();
        w.Slot = targetList.IndexOf(creature);
        w.VisualState = owner == AreaPosition.Low ? VisualStates.LowTable : VisualStates.TopTable;

        creature.SetActive(!isFogged);

        ownerArea?.GetOwnerPlayer()?.ResyncCreatureOrderForArea(
            baseID, MeleeCreaturesOnTable, RangedCreaturesOnTable);

        PlaceCreaturesOnNewSlots(Command.CommandExecutionComplete);
    }

    // Retourne la position dans la rangée (0 = le plus à gauche)
    public int TablePosForNewCreature(bool isMelee)
    {
        CenteredSlots rowSlots = GetRowSlots(isMelee);
        int count = isMelee ? MeleeCreaturesOnTable.Count : RangedCreaturesOnTable.Count;
        int rowPos = RowPosForMouse(count, rowSlots);
        return rowPos;
    }

    // --- Traduction d'index réseau (ghost-free) ---
    // Utilisées uniquement par le pipeline PlayCreature (DragCreatureOnTable, Player.NetworkPendingPlayCreature,
    // PlayACreatureCommand) et comme position de repli avant insertion d'un MoveCreature
    // (voir MoveCreatureToIndex) — l'ordre final d'une rangée avec des déplacements en attente est
    // désormais piloté par ApplyCreatureOrder/SortListByIDs (voir DragCreatureActions.BroadcastRowOrder),
    // pas par ces deux fonctions.
    // Les ghosts de déplacement/pose en attente (voir SpawnPendingMoveGhost / AddCreatureAtIndex
    // appelé depuis NetworkPendingPlayCreature) n'existent que localement, chez le joueur qui a
    // l'action en attente : ils occupent un slot dans MeleeCreaturesOnTable/RangedCreaturesOnTable
    // sur CE client, mais jamais chez les autres. Un index de liste brut n'a donc pas le même sens
    // d'un client à l'autre (ni d'un instant à l'autre, une fois d'autres ghosts créés/détruits).
    // Tout tablePos qui transite par le réseau (ServerRpc/ClientRpc) ou par un buffer différé doit
    // donc être exprimé en index "logique" (ne comptant que les créatures réelles) via
    // ToNetworkTablePos avant l'envoi, puis reconverti en index de liste réel via
    // FromNetworkTablePos juste avant l'insertion sur chaque client.
    public int ToNetworkTablePos(bool isMelee, int rawVisualIndex)
    {
        List<GameObject> row = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;
        int logical = 0;
        for (int i = 0; i < rawVisualIndex && i < row.Count; i++)
        {
            if (!IsGhost(row[i])) logical++;
        }
        return logical;
    }

    public int FromNetworkTablePos(bool isMelee, int logicalIndex)
    {
        List<GameObject> row = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;
        int seen = 0;
        for (int i = 0; i < row.Count; i++)
        {
            if (seen == logicalIndex) return i;
            if (!IsGhost(row[i])) seen++;
        }
        return row.Count;
    }

    private static bool IsGhost(GameObject go)
    {
        OneCreatureManager ocm = go != null ? go.GetComponent<OneCreatureManager>() : null;
        return ocm != null && ocm.IsPendingMoveGhost;
    }

    // ID "permanent" d'un GameObject présent dans une rangée : celui de la créature réelle, ou —
    // pour un ghost de déplacement en attente — l'ID permanent de la créature qu'il représente
    // (CreatureLogic.UniqueCreatureID ne change jamais lors d'un déplacement). Contrairement à
    // IDHolder.GetGameObjectWithID (registre global, un seul GameObject par ID), cette identité
    // doit être résolue localement contre la rangée elle-même — voir SortListByIDs.
    public static int PermanentIDOf(GameObject go)
    {
        if (go == null) return -1;
        OneCreatureManager ocm = go.GetComponent<OneCreatureManager>();
        if (ocm != null && ocm.IsPendingMoveGhost) return ocm.PendingMoveSourceCreatureID;
        IDHolder holder = go.GetComponent<IDHolder>();
        return holder != null ? holder.UniqueID : -1;
    }

    // Debug uniquement : noms lisibles d'une rangée, avec marqueur (ghost) pour les déplacements en attente.
    private static string RowNames(List<GameObject> list)
    {
        return string.Join(",", list.ConvertAll(g =>
        {
            OneCreatureManager ocm = g != null ? g.GetComponent<OneCreatureManager>() : null;
            string name = ocm?.cardAsset != null ? ocm.cardAsset.name : "?";
            return ocm != null && ocm.IsPendingMoveGhost ? $"{name}(ghost)" : name;
        }));
    }

    private int RowPosForMouse(int count, CenteredSlots rowSlots)
    {
        if (count == 0) return 0;

        float leftmostX  = Camera.main.WorldToScreenPoint(rowSlots.GetSlotPosition(0,         count)).x;
        float rightmostX = Camera.main.WorldToScreenPoint(rowSlots.GetSlotPosition(count - 1, count)).x;

        float mouseX = Input.mousePosition.x;
        if (mouseX <= leftmostX)  return 0;
        if (mouseX >= rightmostX) return count;

        for (int j = 0; j < count - 1; j++)
        {
            float leftX  = Camera.main.WorldToScreenPoint(rowSlots.GetSlotPosition(j,     count)).x;
            float rightX = Camera.main.WorldToScreenPoint(rowSlots.GetSlotPosition(j + 1, count)).x;
            if (mouseX >= leftX && mouseX <= rightX)
                return j + 1;
        }

        Debug.LogWarning($"[RowPosForMouse] cas non couvert : mouseX={mouseX:F3}");
        return count;
    }

    public void MoveCreatureAway(GameObject creature)
    {
        if (!MeleeCreaturesOnTable.Remove(creature) && !RangedCreaturesOnTable.Remove(creature))
            Debug.LogWarning($"[MoveCreatureAway] GO '{creature?.name ?? "null"}' introuvable dans les listes de {ownerArea?.baseID}. Il pourrait rester dupliqué dans son ancienne zone.");
        _pendingRowEndCreatures.Remove(creature);
        PlaceCreaturesOnNewSlots();
    }

    // Affiche la créature tout au bout de sa rangée tant que son déplacement vers une autre zone est en
    // attente (résolution en fin de phase Command), sans modifier sa position réelle dans la liste.
    public void MarkPendingMoveAtRowEnd(GameObject creature)
    {
        if (_pendingRowEndCreatures.Contains(creature)) return;
        _pendingRowEndCreatures.Add(creature);
        PlaceCreaturesOnNewSlots();
    }

    // Annule l'affichage "tout au bout" : la créature revient instantanément à sa position réelle,
    // puisque celle-ci n'a jamais changé.
    public void ClearPendingMoveRowEnd(GameObject creature)
    {
        if (_pendingRowEndCreatures.Remove(creature))
            PlaceCreaturesOnNewSlots();
    }

    // Retire et détruit un ghost de déplacement en attente (annulation ou résolution du déplacement).
    public void RemovePendingMoveGhost(GameObject ghost)
    {
        if (ghost == null) return;
        MeleeCreaturesOnTable.Remove(ghost);
        RangedCreaturesOnTable.Remove(ghost);
        Destroy(ghost);
        PlaceCreaturesOnNewSlots();
    }

    public void RemoveCreatureWithID(int IDToRemove)
    {
        GameObject creatureToRemove = IDHolder.GetGameObjectWithID(IDToRemove);
        if (!MeleeCreaturesOnTable.Remove(creatureToRemove))
            RangedCreaturesOnTable.Remove(creatureToRemove);
        Destroy(creatureToRemove);

        // On attend la fin réelle du repositionnement (tween DOMove) avant de libérer la file de
        // commandes : sinon la prochaine attaque de la queue peut démarrer alors que les créatures de
        // la rangée sont encore en train de glisser vers leur nouveau slot, et l'attaquant vise à côté.
        PlaceCreaturesOnNewSlots(Command.CommandExecutionComplete);
    }

    // onComplete (optionnel) : appelé une fois que tous les tweens de repositionnement (mêlée + distance)
    // sont réellement terminés — à utiliser avant de libérer la file de commandes (Command.CommandExecutionComplete),
    // pour qu'une attaque suivante ne se déclenche jamais pendant qu'une créature glisse encore vers son slot.
    void PlaceCreaturesOnNewSlots(System.Action onComplete = null)
    {
        if (CreatureAttackVisual.AnyAttackInFlight)
        {
            // Une attaque est en vol quelque part (peu importe la table) : on ne bouge personne
            // tant qu'elle n'est pas résolue, sinon sa cible pourrait glisser sous la charge/le
            // projectile déjà lancé vers une position figée. Rejoué par FlushPendingRelayout.
            _relayoutPending = true;
            if (onComplete != null) _pendingRelayoutCallbacks.Add(onComplete);
            return;
        }

        DoPlaceCreaturesOnNewSlots(onComplete);
    }

    private void FlushPendingRelayout()
    {
        if (!_relayoutPending) return;
        _relayoutPending = false;

        List<System.Action> callbacks = new List<System.Action>(_pendingRelayoutCallbacks);
        _pendingRelayoutCallbacks.Clear();

        DoPlaceCreaturesOnNewSlots(() =>
        {
            foreach (System.Action callback in callbacks)
                callback();
        });
    }

    private void DoPlaceCreaturesOnNewSlots(System.Action onComplete)
    {
        int meleeGap  = (_previewIndex >= 0 &&  _previewIsMelee) ? _previewIndex : -1;
        int rangedGap = (_previewIndex >= 0 && !_previewIsMelee) ? _previewIndex : -1;

        GameObject meleeExcluded  = (_movingCreature != null && MeleeCreaturesOnTable.Contains(_movingCreature))  ? _movingCreature : null;
        GameObject rangedExcluded = (_movingCreature != null && RangedCreaturesOnTable.Contains(_movingCreature)) ? _movingCreature : null;

        if (onComplete == null)
        {
            PlaceRowOnSlots(MeleeCreaturesOnTable,  GetRowSlots(true), meleeGap,  meleeExcluded);
            PlaceRowOnSlots(RangedCreaturesOnTable, rangedSlots,       rangedGap, rangedExcluded);
            return;
        }

        // +1 le temps d'avoir lancé les deux rangées : évite un déclenchement prématuré si une des deux
        // rangées se termine de façon synchrone (aucune créature à déplacer) avant que l'autre soit lancée.
        int pending = 1;
        void RowDone()
        {
            if (--pending <= 0) onComplete();
        }

        pending++;
        PlaceRowOnSlots(MeleeCreaturesOnTable,  GetRowSlots(true), meleeGap,  meleeExcluded, RowDone);
        pending++;
        PlaceRowOnSlots(RangedCreaturesOnTable, rangedSlots,       rangedGap, rangedExcluded, RowDone);
        RowDone();
    }

    // gapIndex : position du slot virtuel vide dans la rangée effective (sans excluded)
    // excluded : créature en cours de drag, exclue du calcul de slots
    // Les créatures marquées "pending move" (voir _pendingRowEndCreatures) sont affichées tout au
    // bout de la rangée sans que l'ordre réel dans `group` ne soit touché.
    // onComplete : appelé une fois que tous les DOMove de cette rangée sont terminés (ou tués, pour ne
    // jamais bloquer la file de commandes si un tween est interrompu par un repositionnement suivant).
    void PlaceRowOnSlots(List<GameObject> group, CenteredSlots rowSlots, int gapIndex = -1, GameObject excluded = null, System.Action onComplete = null)
    {
        if (group.Count == 0) { onComplete?.Invoke(); return; }

        List<GameObject> displayOrder = new List<GameObject>(group.Count);
        List<GameObject> pendingTail = new List<GameObject>();
        foreach (GameObject go in group)
        {
            if (go == excluded) continue;
            if (_pendingRowEndCreatures.Contains(go))
                pendingTail.Add(go);
            else
                displayOrder.Add(go);
        }
        displayOrder.AddRange(pendingTail);

        int effectiveCount = displayOrder.Count;
        bool hasGap = gapIndex >= 0;
        int virtualCount = effectiveCount + (hasGap ? 1 : 0);
        // On clamp pour éviter que gapIndex > effectiveCount ne décale rien (gap en fin de liste)
        int clampedGap = hasGap ? Mathf.Min(gapIndex, effectiveCount) : -1;

        if (virtualCount == 0 || effectiveCount == 0) { onComplete?.Invoke(); return; }

        int pendingTweens = effectiveCount;
        void TweenDone()
        {
            if (--pendingTweens <= 0) onComplete?.Invoke();
        }

        for (int i = 0; i < effectiveCount; i++)
        {
            int virtualIndex = (hasGap && i >= clampedGap) ? i + 1 : i;
            Vector3 targetPos = rowSlots.GetSlotPosition(virtualIndex, virtualCount);
            displayOrder[i].transform.DOKill();
            Tween t = displayOrder[i].transform.DOMove(targetPos, 0.3f).SetEase(Ease.OutQuad);
            if (onComplete != null)
            {
                bool done = false;
                t.OnComplete(() => { done = true; TweenDone(); });
                t.OnKill(() => { if (!done) TweenDone(); });
            }
        }
    }

    // Preview de déplacement intra-zone : la créature est exclue de la rangée et un slot vide la remplace
    public void ShowMovePreview(GameObject creature)
    {
        _movingCreature = creature;
        bool isMelee = MeleeCreaturesOnTable.Contains(creature);
        _previewIsMelee = isMelee;
        // effectiveCount + 1 gap = count total → les positions de slots sont identiques à l'original
        _previewIndex = TablePosForNewCreature(isMelee);
        PlaceCreaturesOnNewSlots();
    }

    public void ShowInsertPreview(int rowLocalPos, bool isMelee)
    {
        _previewIndex   = rowLocalPos;
        _previewIsMelee = isMelee;
        PlaceCreaturesOnNewSlots();
    }

    private static string FormatIDs(List<GameObject> list)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            IDHolder h = list[i] != null ? list[i].GetComponent<IDHolder>() : null;
            if (i > 0) sb.Append(", ");
            sb.Append(h != null ? h.UniqueID.ToString() : "?");
        }
        return sb.ToString();
    }

    public void ReorderCreature(GameObject creature)
    {
        if (_previewIndex < 0)
            return;

        bool isMelee = MeleeCreaturesOnTable.Contains(creature);
        List<GameObject> targetList = isMelee ? MeleeCreaturesOnTable : RangedCreaturesOnTable;

        string before = FormatIDs(targetList);
        int insertIndex = _previewIndex;
        targetList.Remove(creature);
        targetList.Insert(Mathf.Min(insertIndex, targetList.Count), creature);
        Debug.Log($"[Reorder Local] {(isMelee ? "mêlée" : "distance")} | avant=[{before}] → après=[{FormatIDs(targetList)}]");

        _previewIndex = -1;
        _movingCreature = null;

        // Téléporte immédiatement la créature à sa position finale pour qu'elle réapparaisse au bon endroit
        // CenteredSlots rowSlots = GetRowSlots(isMelee);
        // int finalIndex = targetList.IndexOf(creature);
        // creature.transform.DOKill();
        // creature.transform.position = rowSlots.GetSlotPosition(finalIndex, targetList.Count);

        PlaceCreaturesOnNewSlots();

        ownerArea?.GetOwnerPlayer()?.ResyncCreatureOrderForArea(
            ownerArea.baseID, MeleeCreaturesOnTable, RangedCreaturesOnTable);
    }

    public void ApplyCreatureOrder(int[] meleeIDs, int[] rangedIDs)
    {
        Debug.Log($"[ApplyOrder] baseID={ownerArea?.baseID} avant-melee=[{RowNames(MeleeCreaturesOnTable)}] avant-ranged=[{RowNames(RangedCreaturesOnTable)}] meleeIDs=[{string.Join(",", meleeIDs)}] rangedIDs=[{string.Join(",", rangedIDs)}]");
        _lastKnownMeleeOrder  = meleeIDs;
        _lastKnownRangedOrder = rangedIDs;
        SortListByIDs(MeleeCreaturesOnTable, meleeIDs);
        SortListByIDs(RangedCreaturesOnTable, rangedIDs);
        Debug.Log($"[ApplyOrder] baseID={ownerArea?.baseID} après-melee=[{RowNames(MeleeCreaturesOnTable)}] après-ranged=[{RowNames(RangedCreaturesOnTable)}]");
        PlaceCreaturesOnNewSlots();
        ownerArea?.GetOwnerPlayer()?.ResyncCreatureOrderForArea(
            ownerArea.baseID, MeleeCreaturesOnTable, RangedCreaturesOnTable);
    }

    // Trie list selon l'ordre d'IDs permanents fourni (voir PermanentIDOf), mais UNIQUEMENT parmi les
    // éléments couverts par ids : ils sont réordonnés entre eux (à leurs positions d'origine), tout
    // le reste garde exactement sa position actuelle. Important pour ApplyCreatureOrder/MoveCreatureToIndex :
    // ids peut être périmé par rapport à des créatures insérées entretemps par un pipeline indépendant
    // (ex : une carte jouée depuis la main, voir PlayACreatureCommand) qui ne rafraîchit pas ce cache —
    // un fallback "pousser les éléments inconnus en fin de liste" les déplacerait à tort.
    // Résolution scoped à list elle-même (pas via le registre global IDHolder.GetGameObjectWithID) :
    // un ghost de déplacement en attente partage son ID permanent avec la créature réelle qui l'a
    // créé, ailleurs dans le jeu — seule la correspondance locale importe ici.
    private void SortListByIDs(List<GameObject> list, int[] ids)
    {
        Dictionary<int, GameObject> byPermanentID = new Dictionary<int, GameObject>();
        List<int> matchedSlots = new List<int>();
        for (int i = 0; i < list.Count; i++)
        {
            int id = PermanentIDOf(list[i]);
            if (id == -1 || System.Array.IndexOf(ids, id) < 0 || byPermanentID.ContainsKey(id))
                continue;
            byPermanentID[id] = list[i];
            matchedSlots.Add(i);
        }

        List<GameObject> orderedMatches = new List<GameObject>(matchedSlots.Count);
        foreach (int id in ids)
            if (byPermanentID.TryGetValue(id, out GameObject go))
                orderedMatches.Add(go);

        for (int k = 0; k < matchedSlots.Count; k++)
            list[matchedSlots[k]] = orderedMatches[k];
    }

    public void ClearInsertPreview()
    {
        if (_previewIndex < 0 && _movingCreature == null)
            return;
        _previewIndex = -1;
        _movingCreature = null;
        PlaceCreaturesOnNewSlots();
    }

    public float GetRowWorldZ(bool isMelee) =>
        GetRowSlots(isMelee).transform.position.z;

    public void AddCreatureToPendingZone(CardAsset ca, int uniqueID, int baseID)
    {
        int index = pendingSlots.Children.Length / 2 + PendingCreaturesOnTable.Count;
        GameObject creature = CreateCreatureGO(ca, uniqueID, baseID, pendingSlots.Children[index].transform.position);
        creature.transform.SetParent(pendingSlots.transform);
        PendingCreaturesOnTable.Add(creature);
        OneCreatureManager manager = creature.GetComponent<OneCreatureManager>();
        manager.SetGray(true);
        manager.CanReorderNow = true;
        manager.UpdateGlow();
    }

    private CenteredSlots GetRowSlots(bool isMelee) =>
        (isMelee && meleeSlots != null) ? meleeSlots : rangedSlots;

    private GameObject CreateCreatureGO(CardAsset ca, int uniqueID, int baseID, Vector3 position, int? overrideAttack = null, int? overrideHealth = null)
    {
        GameObject creature = GameObject.Instantiate(GlobalSettings.Instance.CreaturePrefab, position, Quaternion.identity);
        OneCreatureManager manager = creature.GetComponent<OneCreatureManager>();
        manager.BaseID   = baseID;
        manager.cardAsset = ca;
        manager.ReadCreatureFromAsset();
        foreach (Transform t in creature.GetComponentsInChildren<Transform>())
            t.tag = owner.ToString() + "Creature";
        IDHolder id = creature.AddComponent<IDHolder>();
        id.UniqueID = uniqueID;

        // Stats de spawn explicites (ex: token créé pendant la planification d'un combat, voir
        // TokenGenerationSO/PlayACreatureCommand) : prioritaires sur la lecture "live" ci-dessous,
        // qui peut déjà refléter la vie de FIN de combat si EnqueueBattleCommands a tourné avant que
        // cette commande de spawn ne s'exécute.
        if (overrideAttack.HasValue || overrideHealth.HasValue)
        {
            manager.AttackText.text = (overrideAttack ?? ca.Attack).ToString();
            manager.HealthText.text = (overrideHealth ?? ca.MaxHealth).ToString();
        }
        // Si la logique existe déjà et a été modifiée avant que ce visuel soit créé
        // (ex: reveal différé côté réseau, buffs OnPlay résolus avant l'affichage),
        // on affiche les stats actuelles plutôt que les stats imprimées de la carte.
        else if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(uniqueID, out CreatureLogic cl))
        {
            manager.AttackText.text = cl.Attack.ToString();
            manager.HealthText.text = cl.Health.ToString();
        }

        return creature;
    }

    private void CreateGhosts()
    {
        DestroyGhosts();
        SpawnGhostRow(MeleeCreaturesOnTable, meleeGhostCreatures, GetRowSlots(true));
        SpawnGhostRow(RangedCreaturesOnTable, rangedGhostCreatures, rangedSlots);
    }

    private void SpawnGhostRow(List<GameObject> row, List<GameObject> ghostList, CenteredSlots slots)
    {
        foreach (GameObject c in row)
        {
            if (c == null) continue;
            OneCreatureManager ocm = c.GetComponent<OneCreatureManager>();
            if (ocm?.cardAsset == null) continue;

            GameObject ghost = Instantiate(GlobalSettings.Instance.CreaturePrefab,
                c.transform.position, Quaternion.identity, slots.transform);

            OneCreatureManager ghostOcm = ghost.GetComponent<OneCreatureManager>();
            ghostOcm.BaseID = ocm.BaseID;
            ghostOcm.cardAsset = ocm.cardAsset;
            ghostOcm.ReadCreatureFromAsset();
            ghostOcm.HealthText.text = ocm.HealthText.text;
            ghostOcm.isGhost = true;
            ghostOcm.SetGray(true);

            foreach (Transform t in ghost.GetComponentsInChildren<Transform>())
                t.tag = owner.ToString() + "Creature";

            ghostList.Add(ghost);
        }
    }

    private void DestroyGhosts()
    {
        foreach (GameObject g in meleeGhostCreatures) if (g != null) Destroy(g);
        foreach (GameObject g in rangedGhostCreatures) if (g != null) Destroy(g);
        meleeGhostCreatures.Clear();
        rangedGhostCreatures.Clear();
    }

    void OnDestroy()
    {
        DestroyGhosts();
    }


    public void SetOwnerColor(Color color) => glow.GetComponent<Image>().color = color;
}
