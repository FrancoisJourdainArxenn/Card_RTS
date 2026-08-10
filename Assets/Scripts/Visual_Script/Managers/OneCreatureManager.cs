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
    public bool HasPendingMove => isArrowVisible;

    // Ghost de déplacement en attente : placeholder visuel affiché dans la zone cible, permettant au
    // joueur de choisir où l'unité s'insérera dans la rangée une fois le déplacement résolu.
    // Sur le ghost lui-même :
    [HideInInspector] public bool IsPendingMoveGhost = false;
    [HideInInspector] public int PendingMoveSourceCreatureID = -1;
    // Sur la créature réelle en cours de déplacement, référence vers son ghost dans la zone cible :
    [HideInInspector] public GameObject PendingMoveGhost;

    public void DestroyPendingMoveGhost()
    {
        if (PendingMoveGhost == null) return;
        GameObject ghost = PendingMoveGhost;
        PendingMoveGhost = null;

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
            pendingMoveArrowMat = pendingMoveArrow.material;
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

    // Position de repli tant que le ghost n'existe pas encore (fenêtre très courte entre ShowPendingMoveArrow
    // et la création du ghost par SpawnPendingMoveGhost).
    private Vector3 _pendingMoveArrowFallbackTarget;

    public void ShowPendingMoveArrow(Vector3 targetWorldPos)
    {
        if (pendingMoveArrow == null) return;
        _pendingMoveArrowFallbackTarget = targetWorldPos;
        pendingMoveArrow.enabled = true;
        pendingMoveArrow.SetPosition(0, CenterPointPosition + arrowOriginOffset);
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
        // Debug.Log($"[Select] Glow enabled for {gameObject.name}");
    }

    public void Deselect()
    {
        UpdateGlow();
    }

}
