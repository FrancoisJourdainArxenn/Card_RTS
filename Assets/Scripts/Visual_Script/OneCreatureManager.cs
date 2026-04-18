using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;

public class OneCreatureManager : MonoBehaviour 
{
    public CardAsset cardAsset;
    public OneCardManager PreviewManager;
    [Header("Text Component References")]
    public TMP_Text HealthText;
    public TMP_Text AttackText;         
    [Header("Image References")]
    public Image CreatureGraphicImage;
    public Image CreatureFrameImage;
    public Image CreatureGlowImage;
    public Image MeleeImage;


    void Awake()
    {
        if (cardAsset != null)
            ReadCreatureFromAsset();
    }

    private bool canAttackNow = false;
    public bool CanAttackNow
    {
        get
        {
            return canAttackNow;
        }

        set
        {
            canAttackNow = value;
        }
    }

    private bool canMoveNow = false;
    public bool CanMoveNow
    {
        get
        {
            return canMoveNow;
        }

        set
        {
            canMoveNow = value;
        }
    }
    public int BaseID {get; set;}
    public void ReadCreatureFromAsset()
    {
        // Change the card graphic sprite
        CreatureGraphicImage.sprite = cardAsset.CardImage;

        AttackText.text = cardAsset.Attack.ToString();
        HealthText.text = cardAsset.MaxHealth.ToString();

        if(cardAsset.melee)
        {
            MeleeImage.enabled = true;
        }

        if (PreviewManager != null)
        {
            PreviewManager.cardAsset = cardAsset;
            PreviewManager.ReadCardFromAsset();
        }
    }	

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount > 0)
        {
            DamageEffect.CreateDamageEffect(transform.position, amount);
            HealthText.text = healthAfter.ToString();
        }
    }

    public void UpdateCreatureGlow()
    {
        if (CanAttackNow)
            CreatureGlowImage.color = Color.red;
        else if (CanMoveNow)
            CreatureGlowImage.color = Color.green;
        CreatureGlowImage.enabled = CanAttackNow || CanMoveNow;
    }

    public void UpdateTargetableVisual(bool targetable)
    {
        CreatureGraphicImage.color = targetable ? Color.white : Color.gray;
    }

    public void SetGray(bool gray)
    {
        CreatureGraphicImage.color = gray ? Color.gray : Color.white;
        CreatureFrameImage.color = gray ? Color.gray : Color.white;
    }
}
