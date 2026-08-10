using UnityEngine;
using System.Collections;
using DG.Tweening;

public class CreatureMoveVisual : MonoBehaviour
{
    private OneCreatureManager manager;
    private WhereIsTheCardOrCreature w;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        manager = GetComponent<OneCreatureManager>();    
        w = GetComponent<WhereIsTheCardOrCreature>();
    }

    public void Move(int baseID, int tablePos)
    {
        IDHolder id = GetComponent<IDHolder>();
        if (id == null || !CreatureLogic.CreaturesCreatedThisGame.ContainsKey(id.UniqueID))
        {
            Debug.LogError("CreatureMoveVisual: creature logic not found.");
            return;
        }

        CreatureLogic creatureLogic = CreatureLogic.CreaturesCreatedThisGame[id.UniqueID];
        Player owner = creatureLogic.owner;
        PlayerArea startingArea = owner.GetPlayerAreaByID(manager.BaseID);
        PlayerArea targetArea = owner.GetPlayerAreaByID(baseID);
        if (targetArea == null)
        {
            Debug.LogError($"CreatureMoveVisual: no PlayerArea found for baseID={baseID} on player {owner.name}");
            return;
        }

        manager.BaseID = baseID;
        GameObject creatureToRemove = IDHolder.GetGameObjectWithID(id.UniqueID);
        startingArea.tableVisual.MoveCreatureAway(creatureToRemove);
        // tablePos arrive en index logique (ghosts exclus, voir TableVisual.ToNetworkTablePos) : ce
        // n'est qu'une position de repli pour l'insertion initiale — MoveCreatureToIndex retrie
        // ensuite la rangée selon le dernier ordre connu (voir TableVisual.ApplyCreatureOrder), qui
        // seul détermine la position finale réelle.
        int rawTablePos = targetArea.tableVisual.FromNetworkTablePos(creatureLogic.IsMelee, tablePos);
        targetArea.tableVisual.MoveCreatureToIndex(gameObject, id.UniqueID, rawTablePos, baseID);
    }
}
