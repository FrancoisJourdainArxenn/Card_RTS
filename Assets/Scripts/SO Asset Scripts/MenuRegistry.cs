using UnityEngine;

[CreateAssetMenu(fileName = "MenuRegistry", menuName = "Card RTS/MenuRegistry")]
public class MenuRegistry : ScriptableObject
{
    public GameObject[] maps;
    public DeckSO[] decks;
    public bool enemyDetection = false;
}
