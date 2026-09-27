using UnityEngine.EventSystems;
using UnityEngine.UI;

// Ajouté dynamiquement par ChooseOneManager sur chaque carte offerte — évite de toucher
// OneCardManager.OnPointerClick (déjà utilisé pour BuildingShopVisual, sans rapport ici).
public class ChooseOneCardClickHandler : UnityEngine.MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private ChooseOneManager _manager;
    private CardAsset _cardAsset;
    private Image _glowImage;

    public void Init(ChooseOneManager manager, CardAsset cardAsset, Image glowImage)
    {
        _manager = manager;
        _cardAsset = cardAsset;
        _glowImage = glowImage;
    }

    public void OnPointerClick(PointerEventData eventData) => _manager.OnCardPicked(_cardAsset);

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_glowImage != null) _glowImage.enabled = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_glowImage != null) _glowImage.enabled = false;
    }
}
