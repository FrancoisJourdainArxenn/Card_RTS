using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;


public class MainBaseVisual : MonoBehaviour {

    public Player player;
    public OneBaseManager baseManager;
    public AreaPosition owner;
    public TMP_Text HealthText, MainRessourceText, SecondRessourceText;
    public Image fogOverlay;
    [HideInInspector] public bool hasBeenSeen = false;
    private bool currentlyVisible = true;
    
    void Awake()
	{
		if(baseManager != null)
			ApplyLookFromAsset();
	}
	
	public void ApplyLookFromAsset()
    {
        if (!currentlyVisible)
            return;
        HealthText.text = baseManager.HealthText.text;
        MainRessourceText.text = player.mainRessourceAvailable.ToString();
        SecondRessourceText.text = player.secondRessourceAvailable.ToString();
    }

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount > 0)
        {
            Debug.Log("Taking damage: " + amount + " Health after: " + healthAfter);
            if(currentlyVisible)
            {
                DamageEffect.CreateDamageEffect(transform.position, amount);
                HealthText.text = healthAfter.ToString();    
            }            
            if (player == GlobalSettings.Instance.localPlayer)
                GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
        }
    }

    public void Explode()
    {
        Instantiate(GlobalSettings.Instance.ExplosionPrefab, transform.position, Quaternion.identity);
        Sequence s = DOTween.Sequence();
        s.PrependInterval(2f);
        s.OnComplete(() => GlobalSettings.Instance.GameOverPanel.SetActive(true));
    }

    public void ApplyFogForObserver(bool hasVision)
    {
        currentlyVisible = hasVision;
        if (hasVision)
        {
            hasBeenSeen = true;
            gameObject.SetActive(true);
            if (fogOverlay != null)
                fogOverlay.gameObject.SetActive(false);
            ApplyLookFromAsset();
        }
        else
        {
            // Invisible si jamais vu, sinon reste visible dans le dernier état connu
            gameObject.SetActive(hasBeenSeen);
            if (fogOverlay != null)
                fogOverlay.gameObject.SetActive(hasBeenSeen);
        }
    }

}
