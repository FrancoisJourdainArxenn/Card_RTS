using UnityEngine;
using UnityEngine.UI;

public class HoverPreview : MonoBehaviour
{
    private static HoverPreview currentlyViewing = null;
    public GameObject toHideWhilePrewiew;
    [SerializeField] private float alphaHide = 0.3f;
    [SerializeField] public Vector2 previewOffset = new(200f, 50f);
    [SerializeField] private Image enemyGlowImage;
    private Color _savedGlowColor;
    private bool _savedGlowEnabled;
    private bool _enemyGlowActive = false;


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
        TryActivateEnemyGlow();
        if (PreviewsAllowed && ThisPreviewEnabled)
        {
            PreviewThisObject();
            TriggerTooltip();
        }
    }

    void OnMouseExit()
    {
        OverCollider = false;
        TryDeactivateEnemyGlow();
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

        int? attackOverride    = null;
        int? healthOverride    = null;
        int? maxHealthOverride = null;

        IDHolder idHolder = GetComponentInParent<IDHolder>();
        if (idHolder != null && CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out CreatureLogic creature))
        {
            attackOverride    = creature.Attack;
            healthOverride    = creature.Health;
            maxHealthOverride = creature.MaxHealth;
        }

        CardPreviewUI.Instance?.Show(asset, previewOffset, attackOverride, healthOverride, maxHealthOverride);
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
    private void TryActivateEnemyGlow()
    {
        if (enemyGlowImage == null) return;
        Player localPlayer = GlobalSettings.Instance.localPlayer;
        if (localPlayer == null || CompareTag(localPlayer.tag)) return;

        _savedGlowColor = enemyGlowImage.color;
        _savedGlowEnabled = enemyGlowImage.enabled;
        _enemyGlowActive = true;
        enemyGlowImage.color = Color.red;
        enemyGlowImage.enabled = true;
    }

    private void TryDeactivateEnemyGlow()
    {
        if (!_enemyGlowActive) return;
        enemyGlowImage.color = _savedGlowColor;
        enemyGlowImage.enabled = _savedGlowEnabled;
        _enemyGlowActive = false;
    }

}
