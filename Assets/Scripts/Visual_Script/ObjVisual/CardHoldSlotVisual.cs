using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Zone de "réserve" : une carte de main qu'on y dépose y reste indéfiniment (n'est pas
// concernée par Player.DiscardHand en fin de phase Command), tout en restant logiquement
// "en main" (Hand.CardsInHand n'est jamais touché) — elle peut donc être re-draguée et jouée
// exactement comme n'importe quelle autre carte en main. Une seule carte à la fois : en
// déposer une nouvelle renvoie l'ancienne en main.
public class CardHoldSlotVisual : MonoBehaviour
{
    public Player owner;
    public Transform cardAnchor;
    [SerializeField] private LayerMask raycastMask;
    // Décalage appliqué après le bout de la main, exprimé dans l'espace local de HandVisual.slots
    // (donc dans la même orientation que l'éventail de cartes, quelle que soit sa rotation dans la
    // scène) — ajuste-le dans l'Inspector pour créer un petit espace visuel avant cette zone.
    [SerializeField] private Vector3 localOffsetFromHand = Vector3.zero;
    [SerializeField, TextArea(2, 3)] private string emptySlotTooltipText;
    // Icône de cadenas affichée sur la zone : active tant qu'elle est verrouillée (avant tier 3).
    [SerializeField] private Image lockImage;
    [SerializeField] private Image background;
    [SerializeField] private Color lockedBackgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    [SerializeField] private Image glowImage;
    [SerializeField] private Color glowLockedColor = Color.red;
    [SerializeField] private Color glowEmptyColor = Color.green;
    [SerializeField] private Color glowOccupiedColor = Color.yellow;

    private Color _unlockedBackgroundColor;

    private BoxCollider col;
    private bool cursorOverThisSlot;
    private bool _tooltipShown;


    public GameObject HeldCard { get; private set; }
    public bool IsEmpty => HeldCard == null;
    public bool IsLocked => lockImage != null && lockImage.enabled;

    public static bool CursorOverSomeSlot
    {
        get
        {
            foreach (CardHoldSlotVisual s in FindObjectsByType<CardHoldSlotVisual>(FindObjectsSortMode.None))
                if (s.cursorOverThisSlot) return true;
            return false;
        }
    }

    // Le slot précis sous le curseur (ou null) — c'est celui-ci qu'on tente d'utiliser au drop.
    public static CardHoldSlotVisual SlotUnderCursor
    {
        get
        {
            foreach (CardHoldSlotVisual s in FindObjectsByType<CardHoldSlotVisual>(FindObjectsSortMode.None))
                if (s.cursorOverThisSlot) return s;
            return null;
        }
    }

    void Awake()
    {
        col = GetComponent<BoxCollider>();
        if (background != null)
            _unlockedBackgroundColor = background.color;
    }

    private static readonly RaycastHit[] _raycastBuffer = new RaycastHit[8];

    void Update()
    {
        FollowLeftOfHand();
        UpdateLockState();

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int count = Physics.RaycastNonAlloc(ray, _raycastBuffer, 300f, raycastMask, QueryTriggerInteraction.Ignore);
        cursorOverThisSlot = false;
        for (int i = 0; i < count; i++)
            if (_raycastBuffer[i].collider == col) { cursorOverThisSlot = true; break; }

        UpdateGlow();

        bool shouldShowTooltip = cursorOverThisSlot && IsEmpty;
        if (shouldShowTooltip != _tooltipShown)
        {
            if (shouldShowTooltip)
                UITooltip.ShowTooltip_Static(emptySlotTooltipText);
            else
                UITooltip.HideTooltip_Static();
            _tooltipShown = shouldShowTooltip;
        }
    }

    // Tant que owner/homeBaseLogic ne sont pas encore assignés, on ne touche pas à lockImage : elle
    // garde l'état posé dans l'Inspector (actif par défaut). Pas de retour en arrière une fois
    // débloquée : on ne redescend jamais de tier dans ce jeu.
    private void UpdateLockState()
    {
        if (lockImage == null || owner == null || owner.homeBaseLogic == null)
            return;

        bool isLockedNow = owner.homeBaseLogic.CurrentTier < CardTier.T3;
        lockImage.enabled = isLockedNow;

        if (background != null)
            background.color = isLockedNow ? lockedBackgroundColor : _unlockedBackgroundColor;
    }

    // Halo de survol : rouge si verrouillé, vert si déverrouillé et vide, jaune si déverrouillé et occupé.
    private void UpdateGlow()
    {
        if (glowImage == null)
            return;

        if (!cursorOverThisSlot)
        {
            glowImage.enabled = false;
            return;
        }

        glowImage.color = IsLocked ? glowLockedColor
            : IsEmpty ? glowEmptyColor
            : glowOccupiedColor;
        glowImage.enabled = true;
    }

    // Repositionne toute la zone (donc son collider ET cardAnchor, son enfant) juste à gauche de
    // la carte actuellement la plus à gauche de la main. La carte tenue (parentée sous
    // cardAnchor.parent, voir TryHoldCard) suit automatiquement puisqu'elle est enfant de cette
    // même racine.
    private void FollowLeftOfHand()
    {
        if (owner == null || owner.handVisual == null)
            return;

        HandVisual hv = owner.handVisual;
        Vector3 offset = hv.slots.transform.TransformVector(localOffsetFromHand);
        transform.position = hv.PositionLeftOfHand() + offset;
    }

    // Le slot de réserve appartenant à 'p' (un seul par joueur) — utilisé par GameNetworkManager
    // pour rejouer ApplyHold côté réseau sur la bonne instance.
    public static CardHoldSlotVisual ForPlayer(Player p)
    {
        foreach (CardHoldSlotVisual s in FindObjectsByType<CardHoldSlotVisual>(FindObjectsSortMode.None))
            if (s.owner == p) return s;
        return null;
    }

    // Dépose 'card' dans ce slot pour le compte de 'draggingPlayer'. Renvoie false (aucun effet)
    // si ce slot n'appartient pas à ce joueur, s'il est verrouillé, ou si la main est pleine (donc
    // pas de place pour évincer la carte actuellement tenue). En session réseau, la carte est
    // appliquée localement tout de suite (rien ici ne dépend d'un ID généré par le serveur) puis
    // relayée à l'autre machine via SyncHoldCardServerRpc — sans ça, ReservedCard resterait local à
    // cette seule machine et DiscardHand (qui tourne indépendamment sur chaque client en fin de
    // phase Command, voir Player.DiscardHand) finirait par désynchroniser les mains.
    public bool TryHoldCard(GameObject card, Player draggingPlayer)
    {
        if (owner != draggingPlayer)
            return false;

        if (IsLocked)
        {
            new ShowMessageCommand("You need to get to tier 3 to unlock this", 2f).AddToQueue();
            return false;
        }

        if (HeldCard != null && HeldCard != card && !owner.handVisual.HasRoomForOneMore)
        {
            new ShowMessageCommand("Your hand is full", 2f).AddToQueue();
            return false;
        }

        ApplyHold(card);

        if (NetworkSessionData.IsNetworkSession)
        {
            int playerIndex = System.Array.IndexOf(Player.Players, owner);
            int cardID = card.GetComponent<IDHolder>().UniqueID;
            GameNetworkManager.Instance.SyncHoldCardServerRpc(playerIndex, cardID);
        }

        return true;
    }

    // Applique le dépôt de 'card' : logique + visuel, sans revalider ownership/verrou/capacité
    // (déjà fait par TryHoldCard côté machine qui drague). Public pour être rejouée à l'identique
    // depuis GameNetworkManager.SyncHoldCardClientRpc sur les autres machines.
    public void ApplyHold(GameObject card)
    {
        if (HeldCard == card)
        {
            // Redéposée sur son propre slot : le tri visuel peut avoir dérivé (BringToFront pendant
            // le drag), et la position peut avoir dérivé aussi (le drop peut avoir eu lieu n'importe
            // où dans le rayon du collider, pas forcément pile sur cardAnchor) — on remet les deux d'aplomb.
            card.transform.DOLocalMove(cardAnchor.localPosition, 0.2f);
            card.GetComponent<WhereIsTheCardOrCreature>().SetHoldSlotSortingOrder();
            return;
        }

        if (HeldCard != null)
            EvictHeldCardToHand();

        WhereIsTheCardOrCreature w = card.GetComponent<WhereIsTheCardOrCreature>();
        owner.handVisual.RemoveCard(card);
        card.transform.SetParent(cardAnchor.parent);
        card.transform.DOLocalMove(cardAnchor.localPosition, 0.3f);
        w.HoldSlot = this;
        w.VisualState = owner.handVisual.owner == AreaPosition.Low ? VisualStates.LowHand : VisualStates.TopHand;
        w.SetHoldSlotSortingOrder();

        HeldCard = card;
        owner.ReservedCard = CardLogic.CardsCreatedThisGame[card.GetComponent<IDHolder>().UniqueID];
    }

    // À appeler quand la carte tenue quitte le slot pour de bon (jouée normalement).
    public void ReleaseCard(GameObject card)
    {
        if (HeldCard != card)
            return;
        HeldCard = null;
        owner.ReservedCard = null;
    }

    private void EvictHeldCardToHand()
    {
        GameObject evicted = HeldCard;
        evicted.GetComponent<WhereIsTheCardOrCreature>().HoldSlot = null;
        owner.handVisual.AddCard(evicted);
        HeldCard = null;
        owner.ReservedCard = null;
    }
}
