using UnityEngine;

public class HoverPreview : MonoBehaviour
{
    private static HoverPreview currentlyViewing = null;

    private static bool _PreviewsAllowed = true;
    public static bool PreviewsAllowed
    {
        get { return _PreviewsAllowed; }
        set
        {
            _PreviewsAllowed = value;
            if (!_PreviewsAllowed)
                StopAllPreviews();
        }
    }

    private bool _thisPreviewEnabled = false;
    public bool ThisPreviewEnabled
    {
        get { return _thisPreviewEnabled; }
        set
        {
            _thisPreviewEnabled = value;
            if (!_thisPreviewEnabled)
                StopAllPreviews();
        }
    }

    public bool OverCollider { get; set; }

    void OnMouseDown()
    {
        GetComponentInParent<OneCreatureManager>()?.OnCreatureClicked();
        GetComponentInParent<OneBuildingManager>()?.OnBuildingClicked();
    }

    void OnMouseEnter()
    {

        if (BuildingShopVisual.IsOpen) return;
        OverCollider = true;
        if (PreviewsAllowed && ThisPreviewEnabled)
        {
            PreviewThisObject();
            TriggerTooltip();
        }
    }

    void OnMouseExit()
    {
        OverCollider = false;
        if (!PreviewingSomeCard())
            StopAllPreviews();
    }

    void TriggerTooltip()
    {
        CardAsset asset = GetComponentInParent<OneCreatureManager>()?.cardAsset
                       ?? GetComponentInParent<OneBuildingManager>()?.cardAsset;


    }

    void PreviewThisObject()
    {
        StopAllPreviews();
        currentlyViewing = this;

        CardAsset asset = GetComponentInParent<OneCreatureManager>()?.cardAsset
                       ?? GetComponentInParent<OneBuildingManager>()?.cardAsset
                       ?? GetComponentInParent<OneCardManager>()?.cardAsset;

        if (asset != null)
            CardPreviewUI.Instance?.Show(asset);
    }

    private static void StopAllPreviews()
    {
        CardPreviewUI.Instance?.Hide();
        currentlyViewing = null;
    }

    private static bool PreviewingSomeCard()
    {
        if (!PreviewsAllowed) return false;

        HoverPreview[] allHoverBlowups = GameObject.FindObjectsByType<HoverPreview>(FindObjectsSortMode.None);
        foreach (HoverPreview hb in allHoverBlowups)
        {
            if (hb.OverCollider && hb.ThisPreviewEnabled)
                return true;
        }
        return false;
    }
}
