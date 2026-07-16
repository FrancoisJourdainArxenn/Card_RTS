using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;

public class OneCreatureManager : OneLivableManager 
{

    [Header("Pending Move Arrow")]
    [SerializeField] private LineRenderer pendingMoveArrow;
    [SerializeField] private Vector3 arrowOriginOffset = Vector3.zero;
    [SerializeField] private float arrowScrollSpeed = 1f;
    private Material pendingMoveArrowMat;
    [HideInInspector] public bool isGhost = false;
    private bool isArrowVisible = false;


    void Awake()
    {
        if (cardAsset != null)
            ReadCreatureFromAsset();
        if (pendingMoveArrow != null)
            pendingMoveArrowMat = pendingMoveArrow.material;
    }

    private void Update()
    {
        if (!isArrowVisible || pendingMoveArrowMat == null) return;
        float offset = Time.time * arrowScrollSpeed;
        pendingMoveArrowMat.SetTextureOffset("_MainTex", new Vector2(-offset % 1f, 0f));
    }

    public void ReadCreatureFromAsset()
    {
        // Change the card graphic sprite
        art.sprite = cardAsset.CardImage;

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

    public void UpdateGlow()
    {
        glow.enabled = CanMoveNow || CanReorderNow;
        glow.color = CanMoveNow ? Color.green : Color.skyBlue;
    }

    public void SetGray(bool gray)
    {
        art.color = gray ? Color.gray : Color.white;
        frame.color = gray ? Color.gray : Color.white;
    }

    public void SetPending(bool pending)
    {
        art.color = pending ? new Color(0.3f, 0.3f, 0.3f) : Color.white;
    }

    public void OnCreatureClicked()
    {
        if (isGhost) return;

        if (!PhaseEffectPipeline.IsPlayerTargetingComplete(GlobalSettings.Instance.localPlayer))
        {
            IDHolder idHolder = GetComponent<IDHolder>();
            if (idHolder == null)
                return;
            if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out CreatureLogic creature))
                return;
            if (PhaseEffectPipeline.OnEntityClicked(creature))
                return; // consumed by targeting
            // Not an eligible target — fall through to normal handling
        }

        // Debug.Log($"[Click] IsBattlePhase={TurnManager.Instance?.IsBattlePhase}");
        if (!TurnManager.Instance.IsBattlePhase)
            return;

        IDHolder battleIdHolder = GetComponent<IDHolder>();
        // Debug.Log($"[Click] IDHolder={battleIdHolder?.UniqueID}");
        if (battleIdHolder == null)
            return;

        bool found = CreatureLogic.CreaturesCreatedThisGame.TryGetValue(battleIdHolder.UniqueID, out CreatureLogic battleCreature);
        // Debug.Log($"[Click] CreatureFound={found}");
        if (!found)
            return;

        Player localPlayer = GlobalSettings.Instance.localPlayer;
        bool isOwn = localPlayer.playedCards.Creatures.Contains(battleCreature);
        // Debug.Log($"[Click] IsOwnCreature={isOwn}, BaseID={BaseID}");
        if (isOwn) return;

        ZoneCombatResolver resolver = ZoneCombatResolver.FindForBase(BaseID);
        // Debug.Log($"[Click] Resolver={resolver}");
        // resolver?.TryRedirectDamageFrom(battleCreature);
    }

    public void ShowPendingMoveArrow(Vector3 targetWorldPos)
    {
        if (pendingMoveArrow == null) return;
        pendingMoveArrow.enabled = true;
        pendingMoveArrow.SetPosition(0, transform.position + arrowOriginOffset);
        pendingMoveArrow.SetPosition(1, targetWorldPos);
        pendingMoveArrow.enabled = true;
        isArrowVisible = true;
    }

    public void ClearPendingMoveArrow()
    {
        if (pendingMoveArrow != null)
            pendingMoveArrow.enabled = false;
        isArrowVisible = false;
    }

    public void Select()
    {
        glow.enabled = true;
        glow.color = Color.yellow;
        Debug.Log($"[Select] Glow enabled for {gameObject.name}");
    }

    public void Deselect()
    {
        UpdateGlow();
    }

}
