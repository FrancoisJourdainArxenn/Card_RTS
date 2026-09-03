using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;


public class MainBaseVisual : MonoBehaviour, ITargetableVisual {

    public Player player;
    public OneBaseManager baseManager;
    public AreaPosition owner;
    public TMP_Text HealthText, MainRessourceText;
    public Image fogOverlay;
    public Image CurrentTierImage;
    private CardTier _lastShownTier = 0;

    [HideInInspector] public bool hasBeenSeen = false;
    private bool currentlyVisible = true;
    	
	public void ApplyLookFromAsset()
    {
        if (!currentlyVisible)
            return;
        // Read HP from the authoritative logic value so fog refreshes don't overwrite damage.
        HealthText.text = player.Health.ToString();
        if (player.homeBaseLogic != null)
        {
            MainRessourceText.text = "+" + player.homeBaseLogic.EffectiveIncome.ToString();
            baseManager?.SetUnderAttackVisual(player.homeBaseLogic.IsUnderAttack);
        }
        RefreshTierIcon();

    }

    public void TakeDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        Debug.Log("Taking damage: " + amount + " Health after: " + healthAfter);
        if(currentlyVisible)
        {
            VisualFeedbackEffect.CreateDamageEffect(transform.position, amount);
            HealthText.text = healthAfter.ToString();
        }
        // Keep baseManager in sync so any other reader sees the correct value.
        if (baseManager != null && baseManager.HealthText != null)
            baseManager.HealthText.text = healthAfter.ToString();
        if (player == GlobalSettings.Instance.localPlayer)
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
    }

    public void HealDamage(int amount, int healthAfter)
    {
        if (amount <= 0)
            return;

        int currentHealth = Mathf.Min(player.baseAsset.MaxHealth, healthAfter);
        Debug.Log("Healing damage: " + amount + " Health after: " + currentHealth);
        if(currentlyVisible)
        {
            VisualFeedbackEffect.CreateDamageEffect(transform.position, -amount);
            HealthText.text = currentHealth.ToString();
        }
        // Keep baseManager in sync so any other reader sees the correct value.
        if (baseManager != null && baseManager.HealthText != null)
            baseManager.HealthText.text = currentHealth.ToString();
        if (player == GlobalSettings.Instance.localPlayer)
            GlobalSettings.Instance.UiPlayerVisual.RefreshUI();
    }

    // Animation de mort de la base — jouée UNE SEULE FOIS après que TOUTES les attaques de sa zone
    // aient été traitées (voir ZoneCombatResolver.EnqueueBattleCommands, qui enfile
    // MainBaseDeathAnimationCommand après sa boucle de steps, jamais depuis ApplyHealthDisplay
    // directement — sinon un deuxième coup fatal dans la même zone rejouerait l'animation). L'ancien
    // "puis révèle GameOverPanel" a été retiré : GameOverPanel n'est assigné dans aucune scène, et la
    // fin de partie passe désormais par GameOverCommand (message + retour menu).
    // Réutilise VfxManager.PlayDeath() (même mécanisme que CreatureDieCommand/BuildingDieCommand) au
    // lieu d'un champ prefab séparé — le composant VfxManager de ce GameObject a déjà un emplacement
    // dédié pour l'animation de mort, pas besoin d'en dupliquer un.
    public void PlayDeathAnimationAndHide(System.Action onComplete)
    {
        VfxManager vfx = GetComponent<VfxManager>();
        float duration = vfx != null ? vfx.PlayDeath() : 0f;

        gameObject.SetActive(false);

        if (duration <= 0f)
        {
            onComplete?.Invoke();
            return;
        }

        // SetLink cible player.gameObject (qui reste vivant tout le reste de la partie), JAMAIS ce
        // gameObject : il vient d'être désactivé ci-dessus, et SetLink tue le tween dès que l'objet
        // lié est désactivé — lier ce gameObject ferait terminer l'attente instantanément.
        bool fired = false;
        DOVirtual.DelayedCall(duration, () => { fired = true; onComplete?.Invoke(); })
            .SetLink(player.gameObject)
            .OnKill(() => { if (!fired) onComplete?.Invoke(); });
    }

    // Affiche les PV — peuvent être négatifs (overkill du coup fatal, voir ZoneCombatResolver.
    // ComputeRoundOutcome, qui en a besoin pour départager une partie où les deux bases meurent le
    // même round). Ne déclenche PAS l'animation de mort : un round peut infliger plusieurs coups
    // fatals successifs à cette base (aucune limite anti-overkill côté joueur, voir
    // AssignSingleAttack) — la déclencher ici la rejouerait à chaque coup. Voir
    // PlayDeathAnimationAndHide, appelée une seule fois après la fin de la zone.
    public void ApplyHealthDisplay(int healthAfter)
    {
        HealthText.text = healthAfter.ToString();
    }

    void OnMouseDown()
    {
        if (TurnManager.Instance == null) return;
        if (!OnPlayTargetingSession.IsActive && PhaseEffectPipeline.IsComplete) return;
        if (BaseLogic.BasesCreatedThisGame.TryGetValue(player.PlayerID, out BaseLogic homeBase))
            PhaseEffectPipeline.OnEntityClicked(homeBase);
    }

    public void UpdateTargetableVisual(bool targetable, bool targeted = false)
    {
        baseManager?.UpdateTargetableVisual(targetable, targeted);
    }

    public void ClearTargetableVisual()
    {
        baseManager?.ClearTargetableVisual();
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

    void OnEnable()
    {
        BaseLogic.OnUpgradeCostChanged += HandleUpgradeCostChanged;
    }

    void OnDisable()
    {
        BaseLogic.OnUpgradeCostChanged -= HandleUpgradeCostChanged;
    }

    private void HandleUpgradeCostChanged(BaseLogic bl)
    {
        if (player == null || bl != player.homeBaseLogic) return;
        RefreshTierIcon();
    }

    public void RefreshTierIcon()
    {
        if (player?.homeBaseLogic == null || CurrentTierImage == null) return;

        BaseLogic bl = player.homeBaseLogic;
        CurrentTierImage.sprite = bl.CurrentTierIcon;
        if (bl.CurrentTier != _lastShownTier && _lastShownTier != 0)
            ValuePopAnimation.Pop(CurrentTierImage.transform);
        _lastShownTier = bl.CurrentTier;
    }



}
