using UnityEngine;
using System.Collections;
using System.Linq;
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
    // Couleur d'origine du matériau (Sprites-Default expose _Color et gère nativement l'alpha,
    // voir SetArrowAlpha).
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

    // Vrai tant qu'un embarquement (Board) est en attente sur cette créature — pendant de
    // (PendingMoveGhost != null) pour un déplacement classique, mais Board ne crée jamais de ghost
    // (voir DragCreatureActions.Board). Protège le visuel "pending" (voir Player.HighlightPlayableCards)
    // d'un refresh qui l'écraserait sinon.
    [HideInInspector] public bool HasPendingBoard;
    // Référence live vers le transport ciblé par un embarquement en attente — sans ceci, la flèche
    // (voir Update()) n'a qu'un instantané figé de sa position (ShowPendingMoveArrow), qui devient
    // faux dès que la rangée se réorganise (ex: d'autres passagers marqués en bout de rangée).
    [HideInInspector] public OneCreatureManager PendingBoardTarget;

    // Non-null sur les deux tant qu'un déplacement en attente emprunte le réseau de téléporteurs
    // (voir TeleporterNetwork) — la flèche fait alors un crochet par ces deux positions plutôt que
    // de viser directement le ghost. Références live (comme PendingBoardTarget ci-dessus) pour
    // suivre un réordonnancement des téléporteurs dans leur zone respective.
    [HideInInspector] public OneCreatureManager PendingMoveViaSourceTeleporter;
    [HideInInspector] public OneCreatureManager PendingMoveViaDestTeleporter;

    [Header("Pending State Icons")]
    // Les deux sprites partagent le même Image (pendingIcon, voir OneLivableManager) : une créature
    // ne peut jamais être à la fois "jouée de la main en attente" et "en pendingMove".
    [SerializeField] private Sprite pendingPlaySprite; // carte jouée de la main, en attente de confirmation
    [SerializeField] private Sprite pendingMoveSprite; // déplacement en attente (origine uniquement, pas le ghost)

    [Header("Ressource Panel (Home Unit)")]
    // Caché par défaut sur le prefab (voir Card_Board_Unit) — activé uniquement quand cette créature
    // est la HomeUnit d'un joueur (voir Player.RefreshHomeUnitRessourcePanel), sur le même principe
    // que RessourcePanel sur Card_Board_Main-Base (OneBaseManager), mais porté ici par la créature.
    [SerializeField] private GameObject ressourcePanel;
    [SerializeField] private TMP_Text mainRessourceText;
    [SerializeField] private Image mainRessourceBGColor;
    [SerializeField] private Sprite underAttackBGSprite;
    private Sprite _normalRessourceBGSprite;

    [Header("Transport")]
    // Conteneur des petits portraits — un enfant par passager PLUS un pour le transport lui-même,
    // reconstruit à chaque changement (voir RefreshPassengerPortraits). Doit porter un
    // HorizontalLayoutGroup (le glisser-déposer réordonne par sibling index, voir PassengerPortraitDrag).
    // Assigné dans l'Inspecteur sur le prefab de créature (Card_Board_Unit).
    [SerializeField] private RectTransform passengerPortraitsContainer;
    // Prefab custom instancié par passager ET pour le transport lui-même — doit porter un
    // PassengerPortraitView (Image à remplir) ET un PassengerPortraitDrag (glisser-déposer + clic).
    [SerializeField] private GameObject passengerPortraitPrefab;

    // Reconstruit la bande de portraits à partir de CreatureLogic.ManifestOrder (passagers résolus,
    // passagers en attente locale, et le transport lui-même — dans l'ordre choisi par le joueur, voir
    // CreatureLogic.AddToManifest). Appelée à chaque changement de cargaison ou d'ordre : embarquement
    // mis en attente ou annulé (DragCreatureActions.Board/CancelPendingMove), résolution
    // (CreatureMoveVisual.Board/DisembarkCargo, CreatureLogic.DisembarkAt), et reorder UI
    // (CommitManifestOrderFromUI / GameNetworkManager.ReorderManifestClientRpc).
    public void RefreshPassengerPortraits()
    {
        if (passengerPortraitsContainer == null)
        {
            Debug.Log($"[Transport] RefreshPassengerPortraits — abort, passengerPortraitsContainer not assigned on {name}");
            return;
        }

        for (int i = passengerPortraitsContainer.childCount - 1; i >= 0; i--)
            Destroy(passengerPortraitsContainer.GetChild(i).gameObject);

        IDHolder idHolder = GetComponent<IDHolder>();
        if (idHolder == null) return;
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out CreatureLogic transportLogic)) return;

        Debug.Log($"[Transport] RefreshPassengerPortraits — {transportLogic.DisplayName}(ID:{idHolder.UniqueID}) rebuilding manifest=[{string.Join(", ", transportLogic.ManifestOrder)}]");

        foreach (int id in transportLogic.ManifestOrder)
        {
            if (id == idHolder.UniqueID)
                CreatePortrait(transportLogic.ca.CardImage, transportLogic.DisplayName + "_SelfPortrait", id);
            else if (CreatureLogic.CreaturesCreatedThisGame.TryGetValue(id, out CreatureLogic passenger))
                CreatePortrait(passenger.ca.CardImage, passenger.DisplayName + "_Portrait", id);
        }
    }

    private void CreatePortrait(Sprite sprite, string goName, int representedCreatureID)
    {
        if (passengerPortraitPrefab == null)
        {
            Debug.Log($"[Transport] CreatePortrait — abort, passengerPortraitPrefab not assigned on {name}");
            return;
        }

        GameObject portraitGO = Instantiate(passengerPortraitPrefab, passengerPortraitsContainer);
        portraitGO.name = goName;

        PassengerPortraitView view = portraitGO.GetComponent<PassengerPortraitView>();
        if (view != null)
            view.SetSprite(sprite);
        else
            Debug.LogWarning($"[Transport] CreatePortrait — {passengerPortraitPrefab.name} has no PassengerPortraitView component");

        PassengerPortraitDrag drag = portraitGO.GetComponent<PassengerPortraitDrag>();
        if (drag != null)
            drag.Setup(this, representedCreatureID);
        else
            Debug.LogWarning($"[Transport] CreatePortrait — {passengerPortraitPrefab.name} has no PassengerPortraitDrag component");
    }

    // Appelé par PassengerPortraitDrag.OnEndDrag après un réordonnancement local — lit l'ordre final
    // des enfants du conteneur, l'applique localement puis le diffuse (réseau uniquement : en solo
    // rien d'autre n'a besoin d'être informé).
    public void CommitManifestOrderFromUI()
    {
        Debug.Log($"[Transport] CommitManifestOrderFromUI — called on {name}");

        IDHolder idHolder = GetComponent<IDHolder>();
        if (idHolder == null || passengerPortraitsContainer == null)
        {
            Debug.Log($"[Transport] CommitManifestOrderFromUI — abort, idHolder={(idHolder != null ? "ok" : "NULL")}, passengerPortraitsContainer={(passengerPortraitsContainer != null ? "ok" : "NULL")}");
            return;
        }
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out CreatureLogic transportLogic))
        {
            Debug.Log($"[Transport] CommitManifestOrderFromUI — abort, transport ID:{idHolder.UniqueID} not found in CreaturesCreatedThisGame");
            return;
        }

        int[] newOrder = new int[passengerPortraitsContainer.childCount];
        for (int i = 0; i < newOrder.Length; i++)
        {
            PassengerPortraitDrag drag = passengerPortraitsContainer.GetChild(i).GetComponent<PassengerPortraitDrag>();
            newOrder[i] = drag != null ? drag.RepresentedCreatureID : -1;
        }

        Debug.Log($"[Transport] CommitManifestOrderFromUI — {transportLogic.DisplayName}(ID:{idHolder.UniqueID}) new order=[{string.Join(", ", newOrder)}], NetworkSession={NetworkSessionData.IsNetworkSession}");

        transportLogic.SetManifestOrder(newOrder);

        if (NetworkSessionData.IsNetworkSession)
            GameNetworkManager.Instance.ReorderManifestServerRpc(idHolder.UniqueID, newOrder);
    }

    // Débarquement manuel (clic sur le portrait d'un passager) — gratuit et instantané, contrairement
    // à Board/Move : ne coûte aucun mouvement, ne passe pas par le pipeline différé, résolu tout de
    // suite comme GoFace. Voir PassengerPortraitDrag.OnPointerClick.
    public void RequestDisembarkPassenger(int passengerID)
    {
        Debug.Log($"[Transport] RequestDisembarkPassenger — called on {name} for passengerID={passengerID}, CanReorderNow={CanReorderNow}");
        if (!CanReorderNow)
        {
            Debug.Log($"[Transport] RequestDisembarkPassenger — abort, CanReorderNow is false on {name}");
            return;
        }

        IDHolder idHolder = GetComponent<IDHolder>();
        if (idHolder == null)
        {
            Debug.Log($"[Transport] RequestDisembarkPassenger — abort, no IDHolder on {name}");
            return;
        }
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(idHolder.UniqueID, out CreatureLogic transportLogic))
        {
            Debug.Log($"[Transport] RequestDisembarkPassenger — abort, transport ID:{idHolder.UniqueID} not found in CreaturesCreatedThisGame");
            return;
        }
        if (!CreatureLogic.CreaturesCreatedThisGame.TryGetValue(passengerID, out CreatureLogic passengerLogic))
        {
            Debug.Log($"[Transport] RequestDisembarkPassenger — abort, passenger ID:{passengerID} not found in CreaturesCreatedThisGame");
            return;
        }
        if (passengerLogic.TransportCarrierID != idHolder.UniqueID)
        {
            // Passager encore en attente (embarquement pas encore résolu — voir CreatureLogic.Board,
            // qui est seul à fixer TransportCarrierID) : rien à débarquer côté logique, on annule
            // simplement l'embarquement en attente, comme un clic sur la créature elle-même l'aurait fait.
            if (transportLogic.LocalPendingBoardIDs.Contains(passengerID))
            {
                Debug.Log($"[Transport] RequestDisembarkPassenger — {passengerLogic.DisplayName}(ID:{passengerID}) still pending aboard {transportLogic.DisplayName}(ID:{idHolder.UniqueID}), cancelling the pending board instead");
                DragCreatureActions passengerDrag = IDHolder.GetGameObjectWithID(passengerID)?.GetComponentInChildren<DragCreatureActions>();
                if (passengerDrag == null)
                    Debug.Log($"[Transport] RequestDisembarkPassenger — abort, no DragCreatureActions found for pending passenger ID:{passengerID}");
                else
                    passengerDrag.CancelPendingMove(checkCapacity: true);
                return;
            }

            Debug.Log($"[Transport] RequestDisembarkPassenger — abort, passenger {passengerLogic.DisplayName}(ID:{passengerID}) TransportCarrierID={passengerLogic.TransportCarrierID} does not match this transport ID:{idHolder.UniqueID}");
            return;
        }

        PlayerArea targetArea = transportLogic.owner.GetPlayerAreaByID(transportLogic.BaseID);
        if (targetArea == null)
        {
            Debug.Log($"[Transport] RequestDisembarkPassenger — abort, no PlayerArea found for transport BaseID={transportLogic.BaseID}");
            return;
        }

        int rawCountDbg = passengerLogic.IsMelee ? targetArea.tableVisual.MeleeCreaturesOnTable.Count : targetArea.tableVisual.RangedCreaturesOnTable.Count;
        int effCountDbg = targetArea.tableVisual.EffectiveRowCount(passengerLogic.IsMelee);
        Debug.Log($"[Transport] RequestDisembarkPassenger — capacity check for {passengerLogic.DisplayName}(ID:{passengerID}): rawCount={rawCountDbg}, effectiveCount={effCountDbg}, max={GlobalSettings.Instance.MaxCreaturePerRow}, Command.playingQueue={Command.playingQueue}, Command.CommandQueue.Count={Command.CommandQueue.Count}");

        if (!targetArea.tableVisual.RowHasSpace(passengerLogic.IsMelee))
        {
            Debug.Log($"[Transport] RequestDisembarkPassenger — abort, no room in row (isMelee={passengerLogic.IsMelee}) at baseID={targetArea.baseID}");
            new ShowMessageCommand("No room in that zone.", 1f).AddToQueue();
            return;
        }

        bool isMelee = passengerLogic.IsMelee;
        int rawIndex = isMelee ? targetArea.tableVisual.MeleeCreaturesOnTable.Count : targetArea.tableVisual.RangedCreaturesOnTable.Count;
        int networkPos = targetArea.tableVisual.ToNetworkTablePos(isMelee, rawIndex);

        Debug.Log($"[Transport] RequestDisembarkPassenger — {passengerLogic.DisplayName}(ID:{passengerID}) leaving {transportLogic.DisplayName}(ID:{idHolder.UniqueID}) into baseID={targetArea.baseID}, networkPos={networkPos}, rawCountAfterCheck={rawCountDbg}");

        if (NetworkSessionData.IsNetworkSession)
            GameNetworkManager.Instance.DisembarkPassengerServerRpc(passengerID, targetArea.baseID, networkPos);
        else
            passengerLogic.DisembarkAt(targetArea.baseID, networkPos);
    }

    // Halo affiché sur ce transport pendant qu'un joueur fait glisser une unité pouvant l'embarquer —
    // même idée que PlayerArea.tableVisual.SetHighlight, mais sur une créature (voir
    // DragCreatureActions.HighlightBoardableTransports).
    public void SetTransportHighlight(bool active, bool targeted = false)
    {
        if (glow == null) return;
        if (active)
        {
            glow.enabled = true;
            glow.color = targeted ? Color.yellow : Color.cyan;
        }
        else
        {
            UpdateGlow();
        }
    }

    // Vrai si screenPoint (ex: Input.mousePosition) tombe dans le rectangle écran de cette carte —
    // même technique que MultiSelectionManager.SelectUnits (bounds via GetWorldCorners projetés en
    // écran). Utilisé pendant un drag plutôt qu'un raycast physique classique : le collider de la
    // poignée de drag (voir DragCreatureActions/Draggable) suit la souris et masquerait tout ce qui
    // se trouve en-dessous à un raycast physique.
    private static readonly Vector3[] _screenCheckCorners = new Vector3[4];
    // Diagnostic ponctuel — voir MultiSelectionManager.ConfirmGroupMove, appelé uniquement quand
    // aucun transport candidat n'a matché au clic, pour savoir si frame est null / hors hiérarchie
    // active, ou si les bounds calculés sont simplement loin du point testé.
    public string DebugScreenBoundsInfo(Vector2 screenPoint)
    {
        if (frame == null) return $"{name}: frame IS NULL";
        if (!gameObject.activeInHierarchy) return $"{name}: gameObject NOT active in hierarchy";
        frame.rectTransform.GetWorldCorners(_screenCheckCorners);
        Vector2 dmin = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 dmax = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = Camera.main.WorldToScreenPoint(_screenCheckCorners[i]);
            dmin = Vector2.Min(dmin, p);
            dmax = Vector2.Max(dmax, p);
        }
        return $"{name}: screenRect min={dmin} max={dmax} vs testedPoint={screenPoint}";
    }

    public bool IsScreenPointOver(Vector2 screenPoint)
    {
        if (frame == null || !gameObject.activeInHierarchy) return false;

        frame.rectTransform.GetWorldCorners(_screenCheckCorners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = Camera.main.WorldToScreenPoint(_screenCheckCorners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return screenPoint.x >= min.x && screenPoint.x <= max.x && screenPoint.y >= min.y && screenPoint.y <= max.y;
    }

    [Header("Flying")]
    // Décalage vertical/arrière appliqué aux enfants directs de la racine (Canvas/Target/CenterPoint),
    // jamais à la racine elle-même : la racine est repositionnée en permanence par TableVisual (rangées) et
    // CreatureAttackVisual (windup/charge), qui l'ignoreraient sinon à chaque relayout.
    [SerializeField] private float flyingElevation = 0.3f;
    // Recul le long de Z, vers la base du propriétaire (voir Owner) — même logique que le windupBack
    // du windup d'attaque (CreatureAttackVisual), mais permanent et signé selon le camp.
    [SerializeField] private float flyingPullBack = 0.15f;
    // Camp propriétaire de cette créature, fixé par TableVisual juste avant ReadCreatureFromAsset (comme
    // BaseID) : nécessaire pour signer flyingPullBack, le plateau n'étant pas symétrique en rotation
    // (Low et Top partagent le même axe Z monde, "vers l'adversaire" a un signe opposé pour chacun).
    public AreaPosition Owner { get; set; }

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
            _arrowBaseColor = pendingMoveArrowMat.color;
        }
        if (mainRessourceBGColor != null)
            _normalRessourceBGSprite = mainRessourceBGColor.sprite;

        AssignWorldSpaceCanvasCamera();
    }

    // Le Canvas World Space de la carte (portraits de passagers, etc.) n'a jamais worldCamera assigné
    // dans le prefab — impossible à y stocker en dur (référence vers un objet de SCÈNE, absent au
    // moment d'éditer le prefab). Sans lui, GraphicRaycaster ne calcule jamais aucun hit sur ce Canvas
    // : ni clic ni drag (IPointerClickHandler, IBeginDragHandler...) n'atteignent alors le moindre
    // élément UI dessous — voir PassengerPortraitDrag, premier code du projet à en dépendre (le reste
    // du plateau utilise un raycast physique 3D custom, voir TableVisual.Update, qui n'en a pas besoin).
    private void AssignWorldSpaceCanvasCamera()
    {
        foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                canvas.worldCamera = Camera.main;
        }
    }

    // Affiche RessourcePanel (caché par défaut) — appelé uniquement quand cette créature est HomeUnit
    // pour un joueur (voir Player.RefreshHomeUnitRessourcePanel). Idempotent : peut être rappelé sans
    // risque à chaque refresh d'income.
    public void ActivateRessourcePanel()
    {
        if (ressourcePanel != null)
            ressourcePanel.SetActive(true);
    }

    // Même logique que OneBaseManager.RefreshIncomeDisplay/SetUnderAttackVisual, portée ici par la
    // créature — voir Player.RefreshHomeUnitRessourcePanel pour la source (homeBaseLogic.EffectiveIncome/
    // IsUnderAttack, toujours la source d'économie même en mode HomeUnit).
    public void RefreshIncomeDisplay(int income, bool underAttack)
    {
        if (mainRessourceText != null)
            mainRessourceText.text = "+" + income.ToString();
        if (mainRessourceBGColor != null && underAttackBGSprite != null)
            mainRessourceBGColor.sprite = underAttack ? underAttackBGSprite : _normalRessourceBGSprite;
    }

    private void Update()
    {
        if (!isArrowVisible) return;

        // Le point de départ suit la créature (elle peut encore bouger tant que le déplacement est en
        // attente, ex: replacée en bout de rangée) ; le point d'arrivée suit le ghost dans la zone cible
        // une fois créé (il peut lui aussi bouger si le joueur le réordonne), pour relier visuellement
        // les deux éléments.
        pendingMoveArrow.SetPosition(0, CenterPointPosition + arrowOriginOffset);

        if (PendingMoveViaSourceTeleporter != null && PendingMoveViaDestTeleporter != null)
        {
            // Crochet par le téléporteur de départ, puis redirigé vers celui d'arrivée — les deux
            // positions restent live pour suivre un réordonnancement dans leur zone respective.
            pendingMoveArrow.SetPosition(1, PendingMoveViaSourceTeleporter.CenterPointPosition);
            pendingMoveArrow.SetPosition(2, PendingMoveViaDestTeleporter.CenterPointPosition);
        }
        else
        {
            // Un téléporteur emprunté peut mourir pendant qu'un déplacement est en attente : retombe
            // proprement sur une ligne à 2 points (voir positionCount mis à 3 par
            // ShowPendingMoveArrowViaTeleporter) plutôt que de laisser un 3e point figé.
            if (pendingMoveArrow.positionCount != 2)
                pendingMoveArrow.positionCount = 2;

            Vector3 targetPos = PendingMoveGhost != null
                ? PendingMoveGhost.GetComponent<OneCreatureManager>().CenterPointPosition
                : PendingBoardTarget != null
                    ? PendingBoardTarget.CenterPointPosition
                    : _pendingMoveArrowFallbackTarget;
            pendingMoveArrow.SetPosition(1, targetPos);
        }

        if (pendingMoveArrowMat == null) return;
        float offset = Time.time * arrowScrollSpeed;
        pendingMoveArrowMat.mainTextureOffset = new Vector2(-offset % 1f, 0f);
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

        if (cardAsset.Flying)
        {
            // Low → vers l'adversaire = +Z, donc "vers sa propre base" = -Z. Top est l'inverse (voir Owner).
            float backwardZ = (Owner == AreaPosition.Low ? -1f : 1f) * flyingPullBack;
            foreach (Transform child in transform)
            {
                Vector3 pos = child.localPosition;
                child.localPosition = new Vector3(pos.x, flyingElevation, pos.z + backwardZ);
            }
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

    public void SetPending(bool pending, bool isPendingMove = false)
    {
        float v = pendingMoveDarkenAmount;
        art.color = pending ? new Color(v, v, v) : Color.white;
        SetPendingIcon(pending, pending ? (isPendingMove ? pendingMoveSprite : pendingPlaySprite) : null);
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
            {
                // Ce clic vient de résoudre (ou faire avancer) un ciblage — empêche le
                // Draggable.OnMouseDown() de ce même GameObject, qui s'exécute juste après dans le
                // même évènement, de démarrer un drag sur la cible tout juste sélectionnée (voir
                // Draggable.SuppressNextMouseDown).
                Draggable.SuppressNextMouseDown();
                return; // consumed by targeting
            }
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
        if (pendingMoveArrowMat != null)
        {
            Color c = _arrowBaseColor;
            c.a = alpha;
            pendingMoveArrowMat.color = c;
        }
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
        PendingMoveViaSourceTeleporter = null;
        PendingMoveViaDestTeleporter = null;
        pendingMoveArrow.positionCount = 2;
        _pendingMoveArrowFallbackTarget = targetWorldPos;
        pendingMoveArrow.SetPosition(0, CenterPointPosition + arrowOriginOffset);
        pendingMoveArrow.SetPosition(1, targetWorldPos);
        isArrowVisible = true;
        ShowArrowFullyVisible();
        ScheduleArrowFadeAfterDelay(pendingMoveArrowVisibleDuration);
    }

    // Variante empruntant le réseau de téléporteurs (voir TeleporterNetwork) : la flèche fait un
    // crochet par sourceTeleporter (dans la zone de départ) avant de rejoindre destTeleporter (dans
    // la zone d'arrivée), au lieu de viser directement le ghost — voir Update() pour le suivi live.
    public void ShowPendingMoveArrowViaTeleporter(OneCreatureManager sourceTeleporter, OneCreatureManager destTeleporter)
    {
        if (pendingMoveArrow == null) return;
        PendingMoveViaSourceTeleporter = sourceTeleporter;
        PendingMoveViaDestTeleporter = destTeleporter;
        pendingMoveArrow.positionCount = 3;
        pendingMoveArrow.SetPosition(0, CenterPointPosition + arrowOriginOffset);
        pendingMoveArrow.SetPosition(1, sourceTeleporter.CenterPointPosition);
        pendingMoveArrow.SetPosition(2, destTeleporter.CenterPointPosition);
        isArrowVisible = true;
        ShowArrowFullyVisible();
        ScheduleArrowFadeAfterDelay(pendingMoveArrowVisibleDuration);
    }

    public void ClearPendingMoveArrow()
    {
        DOTween.Kill(pendingMoveArrow);
        if (pendingMoveArrow != null)
        {
            pendingMoveArrow.enabled = false;
            pendingMoveArrow.positionCount = 2;
        }
        isArrowVisible = false;
        _isHovered = false;
        _isGhostHovered = false;
        _arrowAlpha = 1f;
        PendingMoveViaSourceTeleporter = null;
        PendingMoveViaDestTeleporter = null;
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
