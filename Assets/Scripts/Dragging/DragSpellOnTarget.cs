using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;

// Un sort ciblé a maintenant une vraie phase de "tenue" avant de démarrer le ciblage : tant que
// le curseur reste dans le cadre de la main (HandBoundsVisual) OU au-dessus d'un
// CardHoldSlotVisual, la carte suit juste la souris (rien n'est commis). Un relâchement dans le
// cadre annule (retour en main) ; un relâchement sur un slot de réserve l'y dépose (comme
// DragCreatureOnTable/DragSpellNoTarget). Dès que le curseur sort des deux, la session de
// ciblage démarre (flèche + clic sur une cible), comme avant.
public class DragSpellOnTarget : DraggingActions {

    private WhereIsTheCardOrCreature whereIsThisCard;
    private OneCardManager manager;
    private IDHolder idHolder;

    private int savedHandSlot;
    private VisualStates tempState;
    [SerializeField] private float dragScale = 0.3f;
    private Vector3 _originalScale;
    private Vector3 _rootStartPos;
    private Vector3 _targetStartPos;
    private Vector3 _targetStartLocalPos;
    private bool _sessionStarted;

    public override bool CanDrag
    {
        get
        {
            return base.CanDrag && manager.CanBeDraggedNow && !OnPlayTargetingSession.IsActive;
        }
    }

    void Awake()
    {
        // Draggable/DragSpellOnTarget vivent sur l'enfant "Target" (qui porte le BoxCollider de
        // détection souris), pas sur la racine de la carte.
        manager = GetComponentInParent<OneCardManager>();
        whereIsThisCard = GetComponentInParent<WhereIsTheCardOrCreature>();
        idHolder = GetComponentInParent<IDHolder>();
    }

    public override void OnStartDrag()
    {
        _sessionStarted = false;
        savedHandSlot = whereIsThisCard.Slot;
        tempState = whereIsThisCard.VisualState;
        whereIsThisCard.VisualState = VisualStates.Dragging;
        whereIsThisCard.BringToFront();

        _originalScale = whereIsThisCard.transform.localScale;
        whereIsThisCard.transform.DOScale(_originalScale * dragScale, 0.0f).SetEase(Ease.OutQuad);

        _rootStartPos = whereIsThisCard.transform.position;
        _targetStartPos = transform.position;
        _targetStartLocalPos = transform.localPosition;
    }

    public override void OnDraggingInUpdate()
    {
        if (Input.GetMouseButtonDown(1))
        {
            Draggable.CancelCurrentDrag();
            return;
        }

        // La racine (qui porte le Canvas) suit le même déplacement que "Target" (que Draggable
        // fait suivre la souris) — voir le commentaire de classe.
        Vector3 delta = transform.position - _targetStartPos;
        whereIsThisCard.transform.position = _rootStartPos + delta;

        bool stillDeciding = HandBoundsVisual.CursorInsideHandOf(playerOwner.handVisual.owner)
            || CardHoldSlotVisual.SlotUnderCursor != null;
        if (!stillDeciding)
            StartTargetingSession();
    }

    private void StartTargetingSession()
    {
        if (_sessionStarted) return;
        _sessionStarted = true;

        if (idHolder == null)
            idHolder = GetComponentInParent<IDHolder>();

        // Le drag est autorisé même sans assez de ressource (voir OneCardManager.CanBeDraggedNow) —
        // pour pouvoir déposer la carte dans un CardHoldSlotVisual malgré tout. On revalide donc le
        // coût ici, avant même de démarrer le ciblage, plutôt que de laisser le joueur choisir une
        // cible pour un sort qu'il ne peut de toute façon pas se permettre.
        if (!CardLogic.CardsCreatedThisGame.TryGetValue(idHolder.UniqueID, out CardLogic cl)
            || cl.MainCost > playerOwner.MainRessourceAvailable)
        {
            new ShowMessageCommand("Not enough resources", 2f).AddToQueue();
            Draggable.EndDragSilently();
            ReturnToHand();
            return;
        }

        List<PendingEffectSelection> requiredSelections =
            OnPlayTargetingSession.CollectRequiredSelections(manager.cardAsset, playerOwner, null);

        if (requiredSelections.Count == 0)
        {
            // Le flag RequiresPlayerInput garantit que la carte a un effet ciblé, pas qu'il existe
            // une cible éligible à cet instant précis (ex: "endommage un ennemi" sans ennemi en jeu).
            new ShowMessageCommand("No valid target", 1.5f).AddToQueue();
            Draggable.EndDragSilently();
            ReturnToHand();
            return;
        }

        int cardID = idHolder.UniqueID;
        foreach (PendingEffectSelection sel in requiredSelections)
        {
            sel.SourceEntityID = cardID;
            sel.IsSpellTargeting = true;
        }

        whereIsThisCard.transform.localScale = _originalScale;
        whereIsThisCard.SetFaceVisible(false);

        OnPlayTargetingSession.Begin(
            requiredSelections,
            onConfirmed: selections =>
            {
                if (whereIsThisCard.HoldSlot != null)
                {
                    whereIsThisCard.HoldSlot.ReleaseCard(whereIsThisCard.gameObject);
                    whereIsThisCard.HoldSlot = null;
                }
                whereIsThisCard.SetFaceVisible(true);
                CommitPlay(selections);
                GetComponent<Draggable>().enabled = false;
            },
            onCancelled: () =>
            {
                whereIsThisCard.SetFaceVisible(true);
                ReturnToHand();
            });

        // La session prend le relais (sélection/annulation par clic ou Echap) : le drag "souris"
        // qui vient de démarrer n'a plus lieu d'être, sinon il continuerait de traîner la carte
        // (donc l'ancre de la flèche) derrière le curseur.
        Draggable.EndDragSilently();
    }

    private void CommitPlay(List<PendingEffectSelection> selections)
    {
        if (idHolder == null)
            idHolder = GetComponentInParent<IDHolder>();

        int cardID = idHolder.UniqueID;

        if (NetworkSessionData.IsNetworkSession)
        {
            int playerIndex = System.Array.IndexOf(Player.Players, playerOwner);
            int[] effectIndexes     = selections.Select(s => s.EffectIndexInCard).ToArray();
            int[] selectedTargetIDs = selections.Select(s => s.SelectedTarget?.ID ?? -1).ToArray();
            GameNetworkManager.Instance.PlaySpellServerRpc(playerIndex, cardID, effectIndexes, selectedTargetIDs);
        }
        else
        {
            CardLogic playedCard = CardLogic.CardsCreatedThisGame[cardID];
            playerOwner.PlayASpellFromHand(playedCard, null, selections);
        }
    }

    // Ne se déclenche que si la session de ciblage n'a jamais démarré, c-à-d si le clic gauche a
    // été relâché dans le cadre de la main ou sur un slot de réserve. Dans ce dernier cas, on y
    // dépose la carte (comme DragCreatureOnTable/DragSpellNoTarget) ; sinon, annulation.
    public override void OnEndDrag()
    {
        if (_sessionStarted)
            return;

        CardHoldSlotVisual targetSlot = CardHoldSlotVisual.SlotUnderCursor;
        if (targetSlot != null && targetSlot.TryHoldCard(whereIsThisCard.gameObject, playerOwner))
        {
            whereIsThisCard.transform.DOScale(_originalScale, 0.3f).SetEase(Ease.OutQuad);
            return;
        }

        ReturnToHand();
    }

    public override void OnDragCancelled()
    {
        if (!_sessionStarted)
            ReturnToHand();
    }

    private void ReturnToHand()
    {
        if (whereIsThisCard.HoldSlot != null)
            whereIsThisCard.SetHoldSlotSortingOrder();
        else
            whereIsThisCard.SetHandSortingOrder();
        whereIsThisCard.VisualState = tempState;
        Vector3 oldCardPos = whereIsThisCard.HoldSlot != null
            ? whereIsThisCard.HoldSlot.cardAnchor.localPosition
            : playerOwner.handVisual.slots.Children[savedHandSlot].transform.localPosition;
        whereIsThisCard.transform.DOLocalMove(oldCardPos, 0.3f);
        whereIsThisCard.transform.DOScale(_originalScale, 0.3f).SetEase(Ease.OutQuad);
        // "Target" (ce script) dérive de sa position pendant le drag : Draggable fixe sa position
        // en absolu chaque frame pendant que le suivi de la racine ci-dessus la déplace aussi (elle
        // en est l'enfant), ce qui la recontamine frame après frame sans jamais se corriger une fois
        // le drag arrêté. On la re-snap donc explicitement à sa position d'origine, sans quoi son
        // collider (donc les clics futurs sur cette carte) reste coincé là où le drag s'est arrêté.
        transform.localPosition = _targetStartLocalPos;
        TurnManager.RefreshAllPlayableHighlights();
    }

    // NOT USED IN THIS SCRIPT
    protected override bool DragSuccessful()
    {
        return true;
    }
}
