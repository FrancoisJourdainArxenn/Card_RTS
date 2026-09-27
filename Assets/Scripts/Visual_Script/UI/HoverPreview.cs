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
        if (OnPlayTargetingSession.IsActive && Input.GetMouseButtonDown(1))
        {
            OnPlayTargetingSession.Cancel();
            return;
        }

        GetComponentInParent<OneCreatureManager>()?.OnCreatureClicked();
    }

    void OnMouseEnter()
    {
        // Carte d'une main cachée (voir HandVisual.HideFromView, ex: main de l'IA face à un joueur
        // humain) : OnMouseEnter réagit au collider physique de la carte, indépendant du CanvasGroup.
        // alpha=0/blocksRaycasts=false posé sur cette même carte (qui ne bloque que le raycast UI) —
        // sans ce garde, survoler l'emplacement (pourtant invisible) déclenchait quand même le popup
        // CardPreviewUI ci-dessous, révélant son contenu réel.
        if (IsHiddenHandCard()) return;
        OverCollider = true;
        TryActivateEnemyGlow();
        GetComponentInParent<OneCreatureManager>()?.SetHovered(true);
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
        GetComponentInParent<OneCreatureManager>()?.SetHovered(false);
        if (!PreviewingSomeCard())
            StopAllPreviews();
    }

    void TriggerTooltip()
    {
        CardAsset asset = GetComponentInParent<OneCreatureManager>()?.cardAsset;


    }

    void PreviewThisObject()
    {
        StopAllPreviews();
        currentlyViewing = this;
        if (_cardCanvasGroup != null) _cardCanvasGroup.alpha = alphaHide;

        CardAsset asset = GetComponentInParent<OneCreatureManager>()?.cardAsset
                    ?? GetComponentInParent<OneCardManager>()?.cardAsset;

        if (asset == null)
        {
            BaseAsset baseAsset = GetComponentInParent<OneBaseManager>()?.baseAsset;
            if (baseAsset != null)
            {
                int? baseHealthOverride = null;
                Player baseOwner = null;
                IDHolder baseIdHolder = GetComponentInParent<IDHolder>();
                if (baseIdHolder != null && BaseLogic.BasesCreatedThisGame.TryGetValue(baseIdHolder.UniqueID, out BaseLogic baseLogic))
                {
                    baseHealthOverride = baseLogic.Health;
                    baseOwner = baseLogic.owner;
                }

                CardPreviewUI.Instance?.ShowBase(baseAsset, previewOffset, baseHealthOverride, baseOwner);
                return;
            }
        }

        Player owner = GetComponentInParent<OneCardManager>()?.owner;

        int? attackOverride    = null;
        int? healthOverride    = null;
        int? maxHealthOverride = null;
        CreatureLogic sourceCreature = null;

        IDHolder idHolder = GetComponentInParent<IDHolder>();
        OneCreatureManager creatureManager = GetComponentInParent<OneCreatureManager>();

        CreatureLogic creature = null;
        if (idHolder != null)
            CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out creature);
        // Ghost de déplacement : même aperçu que la créature d'origine qu'il représente.
        if (creature == null && creatureManager != null && creatureManager.IsPendingMoveGhost)
            CreatureLogic.CreaturesCreatedThisGame.TryGetValue(creatureManager.PendingMoveSourceCreatureID, out creature);

        if (creature != null)
        {
            sourceCreature = creature;
            attackOverride    = creature.Attack;
            healthOverride    = creature.Health;
            maxHealthOverride = creature.MaxHealth;
        }
        else if (creatureManager != null && creatureManager.HasGhostStats)
        {
            attackOverride    = creatureManager.GhostAttack;
            healthOverride    = creatureManager.GhostHealth;
            maxHealthOverride = creatureManager.GhostMaxHealth;
        }
        else if (owner != null && asset.MaxHealth > 0)
        {
            (int bonusAttack, int bonusHealth) = owner.GetPermanentCreatureBuff(asset);
            attackOverride    = Mathf.Max(0, asset.Attack + bonusAttack);
            healthOverride    = asset.MaxHealth + bonusHealth;
            maxHealthOverride = asset.MaxHealth;
        }

        CardPreviewUI.Instance?.Show(asset, previewOffset, owner, attackOverride, healthOverride, maxHealthOverride, sourceCreature);
    }


    private bool IsHiddenHandCard()
    {
        OneCardManager cardManager = GetComponentInParent<OneCardManager>();
        return cardManager != null && cardManager.owner != null && cardManager.owner.handVisual != null
            && cardManager.owner.handVisual.HideFromView;
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
        if (localPlayer == null) return;

        bool isEnemy;

        MainBaseVisual mainBase = GetComponentInParent<MainBaseVisual>();
        if (mainBase != null)
        {
            isEnemy = mainBase.player != localPlayer;
        }
        else
        {
            OneLivableManager livable = GetComponentInParent<OneLivableManager>();
            OneBaseManager baseManager = GetComponentInParent<OneBaseManager>();
            if (livable != null)
                isEnemy = !livable.gameObject.CompareTag(localPlayer.tag);
            else if (baseManager != null)
                isEnemy = !baseManager.gameObject.CompareTag(localPlayer.tag);
            else
                isEnemy = true; // base neutre pas encore construite
        }

        if (!isEnemy) return;

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
