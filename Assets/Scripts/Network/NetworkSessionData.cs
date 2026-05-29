using UnityEngine;

/// <summary>
/// Données réseau persistantes entre les scènes.
/// Rempli dans NetworkMenu avant le chargement de la BattleScene.
/// </summary>
public static class NetworkSessionData
{
    /// <summary>
    /// ClientId Netcode du joueur local (0 = host, 1 = client).
    /// </summary>
    public static ulong LocalClientId { get; set; }

    /// <summary>
    /// Vrai si une session réseau est active (par opposition au jeu local).
    /// </summary>
    public static bool IsNetworkSession { get; set; }

    /// <summary>
    /// Prefab de la map sélectionnée pour la partie, assigné dans NetworkMenu.
    /// </summary>
    public static int SelectedMapIndex { get; set; } = 0;
    
    /// <summary>
    /// Index dans MenuRegistry.decks du preset sélectionné.
    /// -1 = utilise le playerDeck assigné dans la scène (défaut).
    /// </summary>
    public static int SelectedDeckPresetIndex { get; set; } = -1;

}
