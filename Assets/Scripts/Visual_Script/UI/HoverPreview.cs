using UnityEngine;

public class HoverPreview : MonoBehaviour
{
    private static HoverPreview currentlyViewing = null;
    public GameObject toHideWhilePrewiew;
    [SerializeField] private float alphaHide = 0.3f;
    [SerializeField] public Vector2 previewOffset = new(200f, 50f);

    private CanvasGroup _cardCanvasGroup;

    void Start()
    {
        if (toHideWhilePrewiew != null)
            _cardCanvasGroup = toHideWhilePrewiew.GetComponent<CanvasGroup>();
    }


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
        if (_cardCanvasGroup != null) _cardCanvasGroup.alpha = alphaHide;

        CardAsset asset = GetComponentInParent<OneCreatureManager>()?.cardAsset
                    ?? GetComponentInParent<OneBuildingManager>()?.cardAsset
                    ?? GetComponentInParent<OneCardManager>()?.cardAsset;

        CardPreviewUI.Instance?.Show(asset, previewOffset);
    }


    private static void StopAllPreviews()
    {
        CardPreviewUI.Instance?.Hide();

        if (currentlyViewing != null)
        {
            HoverPreview prev = currentlyViewing;
            currentlyViewing = null;
            if (prev._cardCanvasGroup != null) prev._cardCanvasGroup.alpha = 1f;
        }
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
