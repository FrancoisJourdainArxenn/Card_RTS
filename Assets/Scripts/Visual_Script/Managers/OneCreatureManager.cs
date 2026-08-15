using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;
using DG.Tweening;

public class OneCreatureManager : OneLivableManager 
{

    [Header("Pending Move Arrow")]
    [SerializeField] private LineRenderer pendingMoveArrow;
    [SerializeField] private Vector3 arrowOriginOffset = Vector3.zero;
    [SerializeField] private float arrowScrollSpeed = 1f;
    [Range(0f, 1f)]
    [SerializeField] private float pendingMoveDarkenAmount = 0.3f;
    // Combien de temps la flèche reste visible après l'apparition du ghost avant de s'estomper (X).
    [SerializeField] private float pendingMoveArrowVisibleDuration = 2f;
    // Durée du fondu, aussi bien pour ce délai initial que pour la disparition après un hover (Y).
    [SerializeField] private float pendingMoveArrowFadeDuration = 0.5f;
    private Material pendingMoveArrowMat;
    // Couleur d'origine du matériau (voir SetArrowAlpha : le shader graph du laser n'utilise pas le
    // canal alpha classique, on doit donc faire varier toute la couleur pour obtenir un vrai fondu).
    private Color _arrowBaseColor = Color.white;
    [HideInInspector] public bool isGhost = false;
    private bool isArrowVisible = false;
    public bool HasPendingMove => isArrowVisible;

    // La flèche n'est affichée que si la créature d'origine ou son ghost est survolé (voir SetHovered),
    // sauf juste après l'apparition du ghost où elle reste visible pendant pendingMoveArrowVisibleDuration.
    private bool _isHovered = false;
    private bool _isGhostHovered = false;
    private float _arrowAlpha = 1f;

    // Ghost de déplacement en attente : placeholder visuel affiché dans la zone cible, permettant au
    // joueur de choisir où l'unité s'insérera dans la rangée une fois le déplacement résolu.
    // Sur le ghost lui-même :
    [HideInInspector] public bool IsPendingMoveGhost = false;
    [HideInInspector] public int PendingMoveSourceCreatureID = -1;
    // Sur le ghost, référence vers le manager de la créature d'origine, pour relayer le hover (SetHovered).
    [HideInInspector] public OneCreatureManager PendingMoveOrigin;
    // Sur la créature réelle en cours de déplacement, référence vers son ghost dans la zone cible :
    [HideInInspector] public GameObject PendingMoveGhost;

    public void DestroyPendingMoveGhost()
    {
        if (PendingMoveGhost == null) return;
        GameObject ghost = PendingMoveGhost;
        PendingMoveGhost = null;
        SetPending(false); // restaure la couleur normale de la créature d'origine (voir SetPending)

        TableVisual ghostTable = ghost.GetComponentInParent<TableVisual>();
        if (ghostTable != null)
            ghostTable.RemovePendingMoveGhost(ghost);
        else
            Destroy(ghost);
    }


    // Ancre visuelle utilisée pour les flèches (pending move, ciblage...) : le centre réel de la carte,
    // pas la racine du prefab (qui n'est pas centrée dessus). Même pattern que TargetedArrowManager.GetAnchor.
    private Transform _centerPoint;
    public Vector3 CenterPointPosition => _centerPoint.position;

    void Awake()
    {
        Transform center = transform.Find("CenterPoint");
        _centerPoint = center != null ? center : transform;

        if (cardAsset != null)
            ReadCreatureFromAsset();
        if (pendingMoveArrow != null)
        {
            pendingMoveArrowMat = pendingMoveArrow.material;
            _arrowBaseColor = pendingMoveArrowMat.GetColor("_BaseColor");
        }
    }

    private void Update()
    {
        if (!isArrowVisible) return;

        // Le point de départ suit la créature (elle peut encore bouger tant que le déplacement est en
        // attente, ex: replacée en bout de rangée) ; le point d'arrivée suit le ghost dans la zone cible
        // une fois créé (il peut lui aussi bouger si le joueur le réordonne), pour relier visuellement
        // les deux éléments.
        pendingMoveArrow.SetPosition(0, CenterPointPosition + arrowOriginOffset);
        Vector3 targetPos = PendingMoveGhost != null
            ? PendingMoveGhost.GetComponent<OneCreatureManager>().CenterPointPosition
            : _pendingMoveArrowFallbackTarget;
        pendingMoveArrow.SetPosition(1, targetPos);

        if (pendingMoveArrowMat == null) return;
        float offset = Time.time * arrowScrollSpeed;
        pendingMoveArrowMat.SetTextureOffset("_MainTex", new Vector2(-offset % 1f, 0f));
    }

    public void ReadCreatureFromAsset()
    {
        // Change the card graphic sprite
        art.sprite = cardAsset.CardImage;

        AttackText.text = cardAsset.Attack.ToString();
        HealthText.text = cardAsset.MaxHealth.ToString();
        
        if(cardAsset.IsHero)
        {
            HeroSymbol.enabled = true;
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
        float v = pendingMoveDarkenAmount;
        art.color = pending ? new Color(v, v, v) : Color.white;
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

    // Position de repli tant que le ghost n'existe pas encore (fenêtre très courte entre ShowPendingMoveArrow
    // et la création du ghost par SpawnPendingMoveGhost).
    private Vector3 _pendingMoveArrowFallbackTarget;

    // Appelé par HoverPreview (sur la créature d'origine ou sur son ghost) quand la souris entre/sort.
    // Relaye au ghost <-> origine pour que survoler l'un ou l'autre affiche la même flèche.
    public void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        RefreshArrowHoverState();

        if (IsPendingMoveGhost && PendingMoveOrigin != null)
            PendingMoveOrigin.SetGhostHovered(hovered);
    }

    private void SetGhostHovered(bool hovered)
    {
        _isGhostHovered = hovered;
        RefreshArrowHoverState();
    }

    // Un hover (sur l'un ou l'autre bout) montre la flèche à pleine opacité et annule tout fondu en
    // cours ; dès que plus aucun des deux n'est survolé, elle s'estompe immédiatement en Y secondes
    // (sans le délai d'attente initial de ShowPendingMoveArrow).
    private void RefreshArrowHoverState()
    {
        if (!isArrowVisible) return;

        if (_isHovered || _isGhostHovered)
            ShowArrowFullyVisible();
        else
            FadeArrowNow();
    }

    private void SetArrowAlpha(float alpha)
    {
        _arrowAlpha = alpha;
        // Le shader graph du laser expose sa propriété "LaserColor" sous le nom _BaseColor, mais sa
        // sortie Alpha vient d'une conversion implicite Vector4->Vector1 qui ne lit QUE le canal R
        // (pas le vrai canal A) : un simple SetColor(a: alpha) n'a donc aucun effet visuel. On fait
        // varier toute la couleur (R inclus) pour obtenir un vrai fondu.
        if (pendingMoveArrowMat != null)
            pendingMoveArrowMat.SetColor("_BaseColor", _arrowBaseColor * alpha);
        if (pendingMoveArrow != null)
            pendingMoveArrow.enabled = isArrowVisible && alpha > 0f;
    }

    private void ShowArrowFullyVisible()
    {
        DOTween.Kill(pendingMoveArrow);
        SetArrowAlpha(1f);
    }

    // Attend "delay" secondes à pleine opacité puis s'estompe en pendingMoveArrowFadeDuration secondes.
    private void ScheduleArrowFadeAfterDelay(float delay)
    {
        DOTween.Kill(pendingMoveArrow);
        DOTween.Sequence()
            .AppendInterval(delay)
            .Append(DOTween.To(() => _arrowAlpha, SetArrowAlpha, 0f, pendingMoveArrowFadeDuration))
            .SetTarget(pendingMoveArrow);
    }

    private void FadeArrowNow()
    {
        DOTween.Kill(pendingMoveArrow);
        DOTween.To(() => _arrowAlpha, SetArrowAlpha, 0f, pendingMoveArrowFadeDuration)
            .SetTarget(pendingMoveArrow);
    }

    public void ShowPendingMoveArrow(Vector3 targetWorldPos)
    {
        if (pendingMoveArrow == null) return;
        _pendingMoveArrowFallbackTarget = targetWorldPos;
        pendingMoveArrow.SetPosition(0, CenterPointPosition + arrowOriginOffset);
        pendingMoveArrow.SetPosition(1, targetWorldPos);
        isArrowVisible = true;
        ShowArrowFullyVisible();
        ScheduleArrowFadeAfterDelay(pendingMoveArrowVisibleDuration);
    }

    public void ClearPendingMoveArrow()
    {
        DOTween.Kill(pendingMoveArrow);
        if (pendingMoveArrow != null)
            pendingMoveArrow.enabled = false;
        isArrowVisible = false;
        _isHovered = false;
        _isGhostHovered = false;
        _arrowAlpha = 1f;
    }

    public void Select()
    {
        glow.enabled = true;
        glow.color = Color.yellow;
        // Debug.Log($"[Select] Glow enabled for {gameObject.name}");
    }

    public void Deselect()
    {
        UpdateGlow();
    }

}
