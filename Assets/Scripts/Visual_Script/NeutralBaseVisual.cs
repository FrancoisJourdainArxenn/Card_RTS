using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;


public class NeutralBaseVisual : MonoBehaviour {

    public OneBaseManager baseManager;   
    public AreaPosition owner;
    public TMP_Text HealthText;
    
    void Awake()
	{
		if(baseManager != null)
			ApplyLookFromAsset();
	}
	
	public void ApplyLookFromAsset()
    {
        HealthText.text = baseManager.HealthText.text;

    }

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount > 0)
        {
            Debug.Log("Taking damage: " + amount + " Health after: " + healthAfter);
            DamageEffect.CreateDamageEffect(transform.position, amount);
            HealthText.text = healthAfter.ToString();
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
