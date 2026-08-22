using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Un sort ciblé bascule sur le même système que le ciblage OnPlay des créatures
// (OnPlayTargetingSession) au lieu d'un raycast/flèche maison : dès le début du drag, la carte
// disparaît et la flèche de ciblage partagée (EffectTargetingArrow, ancrée sur le SourceEntityID
// résolu — voir EffectTargetingArrow.OnTargetingStarted) apparaît à l'emplacement de la carte en
// main, en attente d'un clic sur une cible éligible. Contrairement aux créatures (qui démarrent
// leur session de ciblage à la FIN du drag, une fois posées sur la table), un sort n'a nulle part
// où être "posé" : la session démarre donc dès OnStartDrag.
public class DragSpellOnTarget : DraggingActions {

    private WhereIsTheCardOrCreature whereIsThisCard;
    private OneCardManager manager;
    private IDHolder idHolder;

    public override bool CanDrag
    {
        get
        {
            return base.CanDrag && manager.CanBePlayedNow && !OnPlayTargetingSession.IsActive;
        }
    }

    void Awake()
    {
        // Draggable/DragSpellOnTarget vivent sur l'enfant "Target" (qui porte le BoxCollider de
        // détection souris), pas sur la racine de la carte — comme l'ancien script.
        manager = GetComponentInParent<OneCardManager>();
        whereIsThisCard = GetComponentInParent<WhereIsTheCardOrCreature>();
        idHolder = GetComponentInParent<IDHolder>();
    }

    public override void OnStartDrag()
    {
        List<PendingEffectSelection> requiredSelections =
            OnPlayTargetingSession.CollectRequiredSelections(manager.cardAsset, playerOwner, null);

        if (requiredSelections.Count == 0)
        {
            // Le flag RequiresPlayerInput garantit que la carte a un effet ciblé, pas qu'il existe
            // une cible éligible à cet instant précis (ex: "endommage un ennemi" sans ennemi en jeu).
            new ShowMessageCommand("No valid target", 1.5f).AddToQueue();
            Draggable.EndDragSilently();
            return;
        }

        int cardID = idHolder.UniqueID;
        foreach (PendingEffectSelection sel in requiredSelections)
            sel.SourceEntityID = cardID;

        whereIsThisCard.SetFaceVisible(false);

        OnPlayTargetingSession.Begin(
            requiredSelections,
            onConfirmed: selections =>
            {
                whereIsThisCard.SetFaceVisible(true);
                CommitPlay(selections);
                GetComponent<Draggable>().enabled = false;
            },
            onCancelled: () =>
            {
                whereIsThisCard.SetFaceVisible(true);
            });

        // La session prend le relais (sélection/annulation par clic ou Echap) : le drag "souris"
        // qui vient de démarrer n'a plus lieu d'être, sinon il continuerait de traîner la carte
        // (donc l'ancre de la flèche) derrière le curseur.
        Draggable.EndDragSilently();
    }

    private void CommitPlay(List<PendingEffectSelection> selections)
    {
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

    // Rien à faire ici : soit aucune session n'a démarré (drag déjà terminé silencieusement dans
    // OnStartDrag), soit une session a démarré et c'est elle qui gère la suite (clic/Echap) —
    // dans les deux cas Draggable.EndDragSilently() a déjà coupé court avant qu'OnEndDrag ne soit
    // atteint via un OnMouseUp normal.
    public override void OnEndDrag() { }

    public override void OnDraggingInUpdate() { }

    // NOT USED IN THIS SCRIPT
    protected override bool DragSuccessful()
    {
        return true;
    }
}
