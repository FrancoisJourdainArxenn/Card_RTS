using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;


public class BaseVisual : MonoBehaviour {

    public Player player;
    public OneBaseManager baseManager;   
    public TMP_Text mainRessourceText,secondRessourceText;
    public TMP_Text MainRessourceIncomeText,SecondRessourceIncomeText;
    public AreaPosition owner;
    public TMP_Text HealthText, UiHealthText;    
    
    void Awake()
	{
		if(baseManager != null)
			ApplyLookFromAsset();
	}
	
	public void ApplyLookFromAsset()
    {
        HealthText.text = baseManager.HealthText.text;
        UiHealthText.text = baseManager.HealthText.text;
        MainRessourceIncomeText.text = baseManager.MainRessourceIncome.text;
        SecondRessourceIncomeText.text = baseManager.SecondRessourceIncome.text;
        mainRessourceText.text = player.mainRessourceAvailable.ToString();
        secondRessourceText.text = player.secondRessourceAvailable.ToString();
        //PortraitImage.sprite = factionAsset.AvatarImage;


    }

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount > 0)
        {
            Debug.Log("Taking damage: " + amount + " Health after: " + healthAfter);
            DamageEffect.CreateDamageEffect(transform.position, amount);
            HealthText.text = healthAfter.ToString();
            UiHealthText.text = healthAfter.ToString();
        }
    }

    public void Explode()
    {
        Instantiate(GlobalSettings.Instance.ExplosionPrefab, transform.position, Quaternion.identity);
        Sequence s = DOTween.Sequence();
        s.PrependInterval(2f);
        s.OnComplete(() => GlobalSettings.Instance.GameOverPanel.SetActive(true));
    }



}
