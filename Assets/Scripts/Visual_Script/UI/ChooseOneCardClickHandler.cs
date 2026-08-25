using UnityEngine.EventSystems;

// Ajouté dynamiquement par ChooseOneManager sur chaque carte offerte — évite de toucher
// OneCardManager.OnPointerClick (déjà utilisé pour BuildingShopVisual, sans rapport ici).
public class ChooseOneCardClickHandler : UnityEngine.MonoBehaviour, IPointerClickHandler
{
    private ChooseOneManager _manager;
    private CardAsset _cardAsset;

    public void Init(ChooseOneManager manager, CardAsset cardAsset)
    {
        _manager = manager;
        _cardAsset = cardAsset;
    }

    public void OnPointerClick(PointerEventData eventData) => _manager.OnCardPicked(_cardAsset);
}
