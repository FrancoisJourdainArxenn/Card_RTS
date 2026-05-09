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
    public Image frame;
    public Image glow;
    public Image MeleeImage;

    [Header("Combat Indicators")]
    public GameObject MarkedForDeathIndicator;
    public GameObject WillBeDamagedIndicator;
    public TMP_Text pendingDamageText;

    private bool canAttackNow = false;
    public bool CanAttackNow
    {
        get { return canAttackNow; }
        set { canAttackNow = value; }
    }
    private bool canMoveNow = false;
    public bool CanMoveNow
    {
        get { return canMoveNow; }
        set { canMoveNow = value; }
    }

    public int BaseID {get; set;}

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;
        
        Debug.Log($"{cardAsset.name} took {amount} damage. {healthAfter} left afterwards.");
        DamageEffect.CreateDamageEffect(transform.position, amount);
        HealthText.text = healthAfter.ToString();
        GetComponentInParent<TableVisual>()?.ownerArea?.RefreshAreaStats();
    }

    public void HealDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;
        
        int currentHealth = Mathf.Min(cardAsset.MaxHealth, healthAfter);
        Debug.Log($"{cardAsset.name} heal {amount} damage. {currentHealth} left afterwards.");
        DamageEffect.CreateDamageEffect(transform.position, -amount);
        HealthText.text = currentHealth.ToString();
        GetComponentInParent<TableVisual>()?.ownerArea?.RefreshAreaStats();
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