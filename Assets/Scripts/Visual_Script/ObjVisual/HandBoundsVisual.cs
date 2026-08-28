using UnityEngine;

// Cadre invisible qui délimite la zone de la main : tant que le curseur y reste pendant un
// drag, la carte est juste "tenue" (rien n'est commis) ; en sortir déclenche le comportement
// propre à la carte (pour DragSpellOnTarget : démarrage de la session de ciblage) ; un
// relâchement de clic gauche alors que le curseur est encore dedans annule le drag.
// Si aucun HandBoundsVisual n'existe pour ce joueur, CursorInsideHandOf renvoie toujours false
// : DragSpellOnTarget dégrade alors vers son ancien comportement (ciblage instantané).
// Basé sur un RectTransform (pas un BoxCollider 3D) : peut donc être placé dans un Canvas, quel
// que soit son Render Mode (Screen Space - Overlay/Camera ou World Space), sans layer à régler.
[RequireComponent(typeof(RectTransform))]
public class HandBoundsVisual : MonoBehaviour
{
    public AreaPosition owner;

    private RectTransform rect;
    private Canvas canvas;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    public static bool CursorInsideHandOf(AreaPosition player)
    {
        foreach (HandBoundsVisual b in FindObjectsByType<HandBoundsVisual>(FindObjectsSortMode.None))
            if (b.owner == player && b.CursorInside()) return true;
        return false;
    }

    private bool CursorInside()
    {
        // Screen Space - Overlay n'a pas de caméra associée ; les autres modes en ont besoin
        // pour convertir correctement la position écran en position dans le Canvas.
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, cam);
    }
}
