using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.SocialPlatforms;

public class BuildingShopVisual : MonoBehaviour
{
    [SerializeField] private GameObject cardBuildingPrefab;
    [SerializeField] private Transform cardContainer;

    public static bool IsOpen {get; private set;}

    public void Show(List<CardAsset> buildings)
    {
        Player localPlayer = GlobalSettings.Instance.localPlayer;

        IsOpen = true;
        foreach (Transform child in cardContainer)
            Destroy(child.gameObject);

        foreach (CardAsset building in buildings)
        {
            GameObject card = Instantiate(cardBuildingPrefab, cardContainer);
            OneCardManager manager = card.GetComponent<OneCardManager>();
            manager.cardAsset = building;
            manager.hoverZoomEnabled = true;
            manager.ReadCardFromAsset();

            bool playable = localPlayer.MainRessourceAvailable >= building.MainCost
                        && localPlayer.SecondRessourceAvailable >= building.SecondCost;
            manager.CanBePlayedNow = playable;
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        IsOpen = false;
        gameObject.SetActive(false);
    }
}
