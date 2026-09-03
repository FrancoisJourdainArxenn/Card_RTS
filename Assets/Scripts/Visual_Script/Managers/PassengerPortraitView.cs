using UnityEngine;
using UnityEngine.UI;

// Contrat minimal pour un prefab de portrait de passager personnalisé — voir
// OneCreatureManager.passengerPortraitPrefab. Le prefab peut avoir n'importe quelle hiérarchie
// (bordure, fond, nom...) tant qu'un composant de ce type est présent quelque part et référence
// l'Image qui doit recevoir le sprite du passager.
public class PassengerPortraitView : MonoBehaviour
{
    [SerializeField] private Image portraitImage;

    public void SetSprite(Sprite sprite)
    {
        if (portraitImage == null) return;
        portraitImage.sprite = sprite;
        portraitImage.preserveAspect = true;
    }
}
