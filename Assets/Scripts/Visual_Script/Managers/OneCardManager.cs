using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;
using TMPro;

// holds the refs to all the Text, Images on the card
public class OneCardManager : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{

    public CardAsset cardAsset;
    public Player owner;
    // Instance vivante correspondant à cardAsset, quand ce OneCardManager représente une créature/
    // bâtiment déjà sur le plateau (pas rempli pour une carte en main) — permet d'afficher la
    // progression d'un CondCounter (source = WrappedCondition), dont le compteur vit sur l'instance.
    public CreatureLogic sourceCreature;
    public BuildingLogic sourceBuilding;
    // public OneCardManager PreviewManager;
    [Header("Text Component References")]
    public TMP_Text NameText;
    public TMP_Text subType;
    public TMP_Text MainCostText;
    public TMP_Text DescriptionText;
    public TMP_Text HealthText;
    public TMP_Text AttackText;
    public GameObject ATK_BG;
    
    [Header("Image References")]
    public Image ArtImage;
    public Image FrameImage;
    public Image CardFaceGlowImage;
    public Image TierImage;
    public Image RowIcon;

    public bool hoverZoomEnabled = false;
    private Vector3 originalScale;
    private Color _originalAttackColor;
    private Color _originalHealthColor;
    private HeroUnlockConditionSO _unlockCondition;
    private bool _wasLocked;

    void Awake()
    {
        originalScale = transform.localScale;
        if (AttackText != null) _originalAttackColor = AttackText.color;
        if (HealthText != null) _originalHealthColor = HealthText.color;

        if (cardAsset != null)
            ReadCardFromAsset();
    }

    private bool canBePlayedNow = false;
    public bool CanBePlayedNow
    {
        get
        {
            return canBePlayedNow;
        }

        set
        {
            canBePlayedNow = value;

            CardFaceGlowImage.enabled = value;
        }
    }

    // Autorise à saisir/déplacer la carte (y compris pour la déposer dans un CardHoldSlotVisual),
    // indépendamment du coût — contrairement à CanBePlayedNow, qui inclut l'affordabilité et pilote
    // le glow. Une carte trop chère doit rester manipulable ; seule sa pose effective (sur la table,
    // ou le lancement d'un sort) reste bloquée par un contrôle de coût séparé au moment du commit.
    public bool CanBeDraggedNow { get; set; }

    public void ReadCardFromAsset()
    {
        NameText.text = string.IsNullOrEmpty(cardAsset.Name) ? cardAsset.name : cardAsset.Name;
        MainCostText.text = cardAsset.MainCost.ToString();
        subType.text = cardAsset.subType.ToString();
        // 4) add description
        DescriptionText.text = cardAsset.GetResolvedDescription(owner);
        // 5) Change the card graphic sprite
        ArtImage.sprite = cardAsset.CardImage;
        if (TierImage != null && VisualManager.Instance != null)
        {
            int tierIndex = (int)cardAsset.tier - 1;
            if (tierIndex >= 0 && tierIndex < VisualManager.Instance.CardTierIcons.Length)
                TierImage.sprite = VisualManager.Instance.CardTierIcons[tierIndex];
        }

        _unlockCondition = cardAsset.IsHero ? cardAsset.UnlockCondition : null;
        PrependGrantedKeywordsText();
        ApplyUnlockDescriptionIfLocked();
        AppendCounterProgressText();

        if (cardAsset.MaxHealth != 0)
        {
            HealthText.text = cardAsset.MaxHealth.ToString();
        }
        if(cardAsset.Attack > 0 || cardAsset.Type == CardType.Unit)
        {
            if (ATK_BG != null) ATK_BG.SetActive(true);
            AttackText.text = cardAsset.Attack.ToString();
        } else { if (ATK_BG != null) ATK_BG.SetActive(false); }

        if (RowIcon != null && GlobalSettings.Instance != null)
            RowIcon.sprite = cardAsset.melee ? GlobalSettings.Instance.MeleeIcon : GlobalSettings.Instance.RangedIcon;

    }

    public void OverrideStats(int? attack, int? health, int? maxHealth = null)
    {
        Color buffColor   = VisualManager.Instance != null ? VisualManager.Instance.statBuffColor   : Color.blue;
        Color debuffColor = VisualManager.Instance != null ? VisualManager.Instance.statDebuffColor : Color.red;

        if (AttackText != null)
        {
            if (attack.HasValue)
            {
                AttackText.text  = attack.Value.ToString();
                AttackText.color = attack.Value > cardAsset.Attack ? buffColor
                                 : attack.Value < cardAsset.Attack ? debuffColor
                                 : _originalAttackColor;
            }
            else
            {
                AttackText.color = _originalAttackColor;
            }
        }

        if (HealthText != null)
        {
            if (health.HasValue && maxHealth.HasValue)
            {
                HealthText.text  = health.Value.ToString();
                HealthText.color = health.Value > maxHealth.Value ? buffColor
                                 : health.Value < maxHealth.Value ? debuffColor
                                 : _originalHealthColor;
            }
            else
            {
                HealthText.color = _originalHealthColor;
            }
        }
    }

    public void ReadEffectFromAsset(string effectName)
    {
        if (NameText != null)        NameText.text = effectName;
        if (DescriptionText != null) DescriptionText.text = cardAsset.GetResolvedDescription(owner);
        if (ArtImage != null)        ArtImage.sprite = cardAsset.CardImage;
    }


    public void ApplyUnlockDescriptionIfLocked()
    {
        if (_unlockCondition != null && owner != null && !_unlockCondition.IsUnlocked(owner))
            DescriptionText.text = _unlockCondition.GetDescription(owner) + "\n\n" + DescriptionText.text;
    }

    // Ajoute (jamais ne remplace), au-dessus du texte de base, un mot-clé en gras pour chaque buff
    // octroyé à l'exécution (Célérité, Piercing/Arcing Attacks...) — même convention que les mots-clés
    // innés déjà écrits à la main dans Description (ex: "<b>Arcing Attacks</b>").
    private void PrependGrantedKeywordsText()
    {
        if (sourceCreature == null) return;

        List<string> grantedNames = new List<string>();

        // Octroyé en jeu UNIQUEMENT ici — l'inné est déjà couvert par le tooltip à icône
        // (CardPreviewUI.BuildTooltipKeywords), jamais réécrit une seconde fois dans la description.
        if (sourceCreature.HasRuntimeCelerity && !cardAsset.Celerity)
        {
            Keyword celerityKeyword = VisualManager.Instance?.CelerityKeyword;
            if (celerityKeyword != null)
                grantedNames.Add(celerityKeyword.displayName);
        }

        if (sourceCreature.HasRuntimeMultiStrike && cardAsset.AttacksForOneTurn <= 1)
        {
            Keyword multiStrikeKeyword = VisualManager.Instance?.MultiStrikeKeyword;
            if (multiStrikeKeyword != null)
                grantedNames.Add(multiStrikeKeyword.displayName);
        }

        foreach (AttackModifierSO modifier in sourceCreature.GrantedAttackModifiers)
        {
            if (modifier?.AssociatedKeyword != null)
                grantedNames.Add(modifier.AssociatedKeyword.displayName);
        }

        if (grantedNames.Count == 0) return;

        string keywordLines = string.Join("\n", grantedNames.ConvertAll(n => $"<b>{n}</b>"));
        DescriptionText.text = keywordLines + "\n\n" + DescriptionText.text;
    }

    // Ajoute (jamais ne remplace) la progression de tout CondCounter présent sur la carte, à la
    // suite du texte déjà en place (description de base, ou texte de déblocage héros ci-dessus).
    private void AppendCounterProgressText()
    {
        if (cardAsset.Effects == null) return;
        foreach (CardEffectData data in cardAsset.Effects)
        {
            if (data.Condition is not CondCounter counter) continue;
            string progress = counter.GetProgressText(sourceCreature, sourceBuilding, owner);
            if (!string.IsNullOrEmpty(progress))
                DescriptionText.text += $" {progress}";
        }
    }

    public void NotifyLockState(bool isLocked)
    {
        if (_wasLocked && !isLocked)
            PopCard();

        _wasLocked = isLocked;
    }

    private Tween _popTween;

    // Ne tue que le punch-scale précédent (évite l'empilement d'échelle en cas de déclenchements
    // rapides), sans toucher aux autres tweens du même transform (repositionnement de main,
    // dépôt dans un CardHoldSlotVisual...) qu'un DOKill() global interromprait en plein vol.
    private void PopCard()
    {
        float strength = VisualManager.Instance != null ? VisualManager.Instance.popStrength : 1f;
        float duration = VisualManager.Instance != null ? VisualManager.Instance.popDuration : 1f;

        _popTween?.Kill();
        transform.localScale = originalScale;
        _popTween = transform.DOPunchScale(originalScale * strength, duration, 1, 0.5f);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (BuildingShopVisual.IsOpen && hoverZoomEnabled)
            transform.DOScale(originalScale * 1.1f, 0.15f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (BuildingShopVisual.IsOpen && hoverZoomEnabled)
            transform.DOScale(originalScale, 0.15f);
    }


    public void OnPointerClick(PointerEventData eventData)
    {
        if (!BuildingShopVisual.IsOpen) return;
        if (!canBePlayedNow) return;
        GlobalSettings.Instance.buildingShop.OnBuildingSelected(cardAsset);
    }

}
