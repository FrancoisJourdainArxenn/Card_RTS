using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OneLivableManager : MonoBehaviour, ITargetableVisual
{
    public CardAsset cardAsset;
    public OneCardManager PreviewManager;
    
    [Header("Text Component References")]
    public TMP_Text HealthText;
    public TMP_Text AttackText;
    
    [Header("Image References")]
    public Image art;
    public GameObject artMask;
    public Image frame;
    public Image glow;
    public Image HeroSymbol;
    public Image pendingIcon;

    private Color _originalAttackColor;
    private Color _originalHealthColor;
    private bool _colorsCached = false;

    private void EnsureColorsCached()
    {
        if (_colorsCached) return;
        if (AttackText != null) _originalAttackColor = AttackText.color;
        if (HealthText != null) _originalHealthColor = HealthText.color;
        _colorsCached = true;
    }

    protected void ApplyStatColor(TMP_Text text, int current, int baseValue, Color originalColor)
    {
        if (text == null || VisualManager.Instance == null) return;
        text.color = current > baseValue ? VisualManager.Instance.statBuffColor
                   : current < baseValue ? VisualManager.Instance.statDebuffColor
                   : originalColor;
    }

    private bool canMoveNow = false;
    public bool CanMoveNow
    {
        get { return canMoveNow; }
        set { canMoveNow = value; }
    }

    private bool canReorderNow = false;
    public bool CanReorderNow
    {
        get { return canReorderNow; }
        set { canReorderNow = value; }
    }
    public int BaseID {get; set;}

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        EnsureColorsCached();
        Debug.Log($"{cardAsset.name} took {amount} damage. {healthAfter} left afterwards.");
        if (gameObject.activeInHierarchy)
            VisualFeedbackEffect.CreateDamageEffect(transform.position, amount);
        HealthText.text = healthAfter.ToString();
        ApplyStatColor(HealthText, healthAfter, cardAsset.MaxHealth, _originalHealthColor);
    }

    public void HealDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        EnsureColorsCached();
        Debug.Log($"{cardAsset.name} heal {amount} damage. {healthAfter} left afterwards.");
        HealthText.text = healthAfter.ToString();
        ApplyStatColor(HealthText, healthAfter, cardAsset.MaxHealth, _originalHealthColor);
    }

    public void BuffStats(int attackAmount, int secondAmount, int attackAfter, int healthAfter)
    {
        EnsureColorsCached();
        if (attackAmount != 0)
        {
            AttackText.text = attackAfter.ToString();
            ApplyStatColor(AttackText, attackAfter, cardAsset.Attack, _originalAttackColor);
            ValuePopAnimation.Pop(AttackText.transform);

        }
        if (secondAmount != 0)
        {
            HealthText.text = healthAfter.ToString();
            ApplyStatColor(HealthText, healthAfter, cardAsset.MaxHealth, _originalHealthColor);
            ValuePopAnimation.Pop(HealthText.transform);
        }
    }

    public void SetPendingIcon(bool visible, Sprite sprite = null)
    {
        if (pendingIcon == null) return;
        if (sprite != null) pendingIcon.sprite = sprite;
        pendingIcon.enabled = visible;
    }

    public void SetVisible(bool visible)
    {
        if (artMask != null)    artMask.SetActive(visible);
        else if (art != null)   art.enabled        = visible;
        if (frame != null)      frame.enabled      = visible;
        if (HeroSymbol != null) HeroSymbol.enabled = visible && cardAsset != null && cardAsset.IsHero;
        if (AttackText != null) AttackText.enabled = visible;
        if (HealthText != null) HealthText.enabled = visible;
        if (glow != null && visible == false)
            glow.enabled = false;
    }

    public virtual void UpgdateGlow() {}
    
    public void UpdateTargetableVisual(bool targetable, bool targeted = false)
    {
        art.color = targetable ? Color.white : Color.gray;
        glow.color = targeted ? Color.green : Color.yellow;
        glow.enabled = targetable;
    }

    public void UpdateUsingEffectVisual()
    {
        glow.color = Color.purple;
        glow.enabled = true;
    }

    public void ClearTargetableVisual()
    {
        glow.enabled = false;
        art.color = Color.white;
    }
}