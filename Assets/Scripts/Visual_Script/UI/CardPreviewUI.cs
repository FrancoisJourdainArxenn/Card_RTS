using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class CardPreviewUI : MonoBehaviour
{
    public static CardPreviewUI Instance { get; private set; }
    [Header("Preview Settings")]
    public float previewScale = 1f;

    public Transform previewAnchor;

    private GameObject currentPreview;
    private GameObject currentPrefab;
    [SerializeField] private GameObject cardPreviewPrefab;
    [SerializeField] private GameObject heroCardPreviewPrefab;
    [SerializeField] private GameObject actionCardPreviewPrefab; // Card_Action_Preview — sorts (CardType.Action)
    [SerializeField] private GameObject effectTriggeredPrefab; // prefab Effect_Triggered ; seule source d'Instantiate pour tous les popups d'effet (parties 1, 2 et 3)
    private RectTransform _anchorRect;
    private Canvas _canvas;
    private bool previewingEffects = false;

    // [SerializeField] private Material _opponentHologramMaterial; // orphelin — parties 1 et 3 désactivées, seules utilisatrices

    [SerializeField] private RectTransform targetingAnchor;
    [SerializeField] private float stackScaleFactor  = 0.85f;
    [SerializeField] private float stackDisplayScale = 0.75f;
    [SerializeField] private float stackOffsetX      = 15f;
    [SerializeField] private float stackOffsetY      = -15f;

    [Header("Trail")]
    [SerializeField] private GameObject trailPrefab;
    [SerializeField] private Transform trailTarget;
    [SerializeField] private float stackAppearDelay = 0.5f;
    public float StackAppearDelay => stackAppearDelay;


    [Header("Hover Arrow")]
    [SerializeField] private HoverArrow hoverArrow;
    [SerializeField] private Transform arrowEndPoint;

    private List<GameObject> _targetingPreviews  = new List<GameObject>();
    private List<GameObject> _autoEffectPreviews = new List<GameObject>(); // pile des popups affichés par ce chemin (partagée avec HandleEffectsBatch, partie 3)
    // private Coroutine _autoEffectDismissCoroutine; // référence à AutoDismissStack en cours, pour ne jamais en lancer deux en parallèle — partie 1 désactivée
    // private bool _batchActive = false; // partie 3 désactivée — plus lu ni écrit nulle part
    private bool _buildingStack = false;
    // [SerializeField] private float autoEffectDisplayDuration = 0f; // durée d'affichage de chaque popup dans ce chemin (voir AutoDismissStack) — 1.5s dans BattleScene — partie 1 désactivée

    void Awake()
    {
        Instance = this;
        _anchorRect = previewAnchor as RectTransform;
        _canvas = previewAnchor.GetComponentInParent<Canvas>();
    }

    void OnEnable()
    {
        TargetingVisualEvents.OnTargetingStarted    += HandleTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded      += HandleTargetingEnded;
        // TargetingVisualEvents.OnAutoEffectTriggered += HandleAutoEffect; // partie 1 désactivée
        // TargetingVisualEvents.OnEffectsBatchPending  += HandleEffectsBatch;       // partie 3 désactivée
        // TargetingVisualEvents.OnEffectResolved       += HandleEffectResolved;     // partie 3 désactivée
        // TargetingVisualEvents.OnEffectsBatchComplete += HandleEffectsBatchComplete; // partie 3 désactivée
    }

    void OnDisable()
    {
        TargetingVisualEvents.OnTargetingStarted    -= HandleTargetingStarted;
        TargetingVisualEvents.OnTargetingEnded      -= HandleTargetingEnded;
        // TargetingVisualEvents.OnAutoEffectTriggered -= HandleAutoEffect; // partie 1 désactivée
        // TargetingVisualEvents.OnEffectsBatchPending  -= HandleEffectsBatch;       // partie 3 désactivée
        // TargetingVisualEvents.OnEffectResolved       -= HandleEffectResolved;     // partie 3 désactivée
        // TargetingVisualEvents.OnEffectsBatchComplete -= HandleEffectsBatchComplete; // partie 3 désactivée
    }

    private ZoneLogic GetCreatureZone(CreatureLogic creature)
    {
        foreach (PlayerArea pa in creature.owner.PAreas)
            if (pa.baseID == creature.BaseID)
                return pa.parentZone.Logic;
        return null;
    }
    
    private bool IsEntityZoneVisible(ILivable entity, List<ZoneLogic> visibleZones)
    {
        if (entity == null) return false;
        ZoneLogic zone = entity switch
        {
            CreatureLogic c => GetCreatureZone(c),
            BuildingLogic b => b.OriginSpot.Zone.Logic,
            BaseLogic     b => b.Zone,
            _               => null
        };
        return zone != null && visibleZones.Contains(zone);
    }

    
    private bool IsEffectVisibleToLocalPlayer(EffectContext context)
    {
        Player local = GlobalSettings.Instance.localPlayer;
        if (context.Caster == local) return true;

        List<ZoneLogic> visible = local.VisibleZones;
        return IsEntityZoneVisible(context.Source, visible)
            || IsEntityZoneVisible(context.Target, visible)
            || (context.TargetedZone != null && visible.Contains(context.TargetedZone));
    }
    
    private void HandleTargetingStarted(List<PendingEffectSelection> queue, int currentIndex)
    {
        // Le ciblage de sort a sa propre flèche dédiée (voir SpellTargetingArrow) et n'a jamais de
        // popup d'effet associé (CreateTargetingPreview exige un OneCreatureManager/OneBuildingManager) :
        // rien à faire ici pour ce cas.
        if (queue[currentIndex].IsSpellTargeting)
            return;

        int remaining = queue.Count - currentIndex;

        if (queue.Count > currentIndex && !IsEffectVisibleToLocalPlayer(queue[currentIndex].Context))
            return;

        ClearAutoStack();

        if (_targetingPreviews.Count == 0 && remaining > 0 && !_buildingStack)
        {
            _buildingStack = true;
            StartCoroutine(BuildStackDelayed(queue, currentIndex, remaining));
        }
        else if (_targetingPreviews.Count > remaining)
        {
            PopFront();

            // Mettre à jour la source de la flèche pour le nouvel effet en tête
            if (remaining > 0 && hoverArrow != null && arrowEndPoint != null)
            {
                GameObject newFrontSourceGO = IDHolder.GetGameObjectWithID(queue[currentIndex].SourceEntityID);
                if (newFrontSourceGO != null)
                    hoverArrow.Show(newFrontSourceGO.transform, arrowEndPoint);
            }
        }
        // equal count = target selected/deselected, no stack change
    }

    private IEnumerator BuildStackDelayed(List<PendingEffectSelection> queue, int currentIndex, int remaining)
    {
        for (int i = 0; i < remaining; i++)
        {
            GameObject sourceGO = IDHolder.GetGameObjectWithID(queue[currentIndex + i].SourceEntityID);
            if (sourceGO != null)
                SpawnTrail(sourceGO.transform);
        }
        
        yield return new WaitForSeconds(stackAppearDelay);

        GameObject frontSourceGO = IDHolder.GetGameObjectWithID(queue[currentIndex].SourceEntityID);
        if (hoverArrow != null && frontSourceGO != null && arrowEndPoint != null)
            hoverArrow.Show(frontSourceGO.transform, arrowEndPoint);

        BuildStack(queue, currentIndex, remaining);
        _buildingStack = false;
    }

    // Partie 1 (popup pour effet auto isolé) désactivée : plus aucun appelant (event commenté
    // dans TargetingVisualEvents.cs et abonnement commenté dans OnEnable/OnDisable ci-dessus).
    // private void HandleAutoEffect(CardEffectData data, EffectContext context)
    // {
    //     // Un batch de phase (partie 3) est déjà en cours de traitement : on ignore cet effet
    //     // auto isolé pour ne pas mélanger les deux systèmes de pile en même temps.
    //     if (_batchActive) return;
    //
    //     // Filtre de brouillard de guerre / visibilité multijoueur : si ni la source, ni la cible,
    //     // ni la zone ciblée de cet effet ne sont visibles pour GlobalSettings.Instance.localPlayer,
    //     // on n'affiche rien du tout côté de ce client.
    //     if (!IsEffectVisibleToLocalPlayer(context)) return;
    //
    //     // Une pile de sélection manuelle (partie 2) est active : elle a priorité d'affichage,
    //     // on ne vient pas superposer un popup auto par-dessus.
    //     if (_targetingPreviews.Count > 0) return;
    //
    //     // Récupère la CardAsset de la source (créature OU bâtiment — un seul des deux est non-null).
    //     CardAsset ca = (context.Source as CreatureLogic)?.ca
    //                 ?? (context.Source as BuildingLogic)?.ca;
    //     if (ca == null) return; // pas de carte associée à la source → rien à afficher
    //
    //     // Récupère l'ID unique de la source, pour pouvoir retrouver son GameObject dans la scène
    //     // plus tard (IDHolder.GetGameObjectWithID), utilisé pour lire la CardAsset ET pour l'animation
    //     // de trail (SpawnTrail).
    //     int sourceID = (context.Source as CreatureLogic)?.UniqueCreatureID
    //                 ?? (context.Source as BuildingLogic)?.UniqueBuildingID
    //                 ?? -1;
    //
    //     // Si l'effet appartient à l'adversaire du joueur local, on applique le matériau "hologramme"
    //     // (silhouette) sur le popup plutôt que le visuel normal de la carte — pour ne pas révéler
    //     // le détail exact de la carte adverse.
    //     Material mat = context.Caster != GlobalSettings.Instance.localPlayer
    //         ? _opponentHologramMaterial
    //         : null;
    //
    //     // Empile ce nouvel effet dans la pile "auto" — voir PushToAutoStack.
    //     PushToAutoStack(new PendingEffectSelection
    //     {
    //         Data            = data,
    //         Context         = context,
    //         EligibleTargets = new List<IIdentifiable>(), // vide : pas de sélection de cible ici (chemin 1)
    //         SourceEntityID  = sourceID
    //     }, mat);
    // }


    // Partie 3 (popup de batch de résolution de phase) désactivée : plus aucun appelant
    // (events commentés dans TargetingVisualEvents.cs, abonnements commentés ci-dessus).
    // private void HandleEffectsBatch(List<PendingEffectSelection> effects, List<bool> hasVisuals)
    // {
    //     _batchActive = true;
    //     if (_targetingPreviews.Count > 0) return;
    //
    //     ClearAutoStack();
    //
    //     for (int i = 0; i < effects.Count; i++)
    //     {
    //         if (hasVisuals != null && i < hasVisuals.Count && !hasVisuals[i]) continue;
    //         if (!IsEffectVisibleToLocalPlayer(effects[i].Context)) continue;
    //
    //         GameObject preview = CreateTargetingPreview(effects[i]);
    //         if (preview == null) continue;
    //
    //         Material mat = effects[i].Context.Caster != GlobalSettings.Instance.localPlayer
    //             ? _opponentHologramMaterial : null;
    //         if (mat != null) ApplyMaterialToHologram(preview, mat);
    //
    //         preview.transform.localPosition = Vector3.zero;
    //         preview.transform.localScale    = Vector3.one * previewScale * stackDisplayScale * 0.5f;
    //         _autoEffectPreviews.Add(preview);
    //
    //         GameObject sourceGO = IDHolder.GetGameObjectWithID(effects[i].SourceEntityID);
    //         if (sourceGO != null) SpawnTrail(sourceGO.transform);
    //     }
    //     RefreshAllStacks();
    // }
    //
    // private void HandleEffectResolved()
    // {
    //     if (_autoEffectPreviews.Count == 0) return;
    //     GameObject front = _autoEffectPreviews[0];
    //     _autoEffectPreviews.RemoveAt(0);
    //     if (front != null) Destroy(front);
    //     RefreshAllStacks();
    // }
    //
    // private void HandleEffectsBatchComplete()
    // {
    //     _batchActive = false;
    //     if (_autoEffectPreviews.Count == 0 && _targetingPreviews.Count == 0)
    //         previewingEffects = false;
    // }

    private void BuildStack(List<PendingEffectSelection> queue, int currentIndex, int remaining)
    {
        for (int i = remaining - 1; i >= 0; i--)
        {
            GameObject preview = CreateTargetingPreview(queue[currentIndex + i]);
            if (preview == null) continue;
            _targetingPreviews.Insert(0, preview);
            preview.transform.localPosition = Vector3.zero;
            preview.transform.localScale    = Vector3.one * previewScale * stackDisplayScale * 0.5f;
        }
        RefreshAllStacks();
    }

    private void PopFront()
    {
        if (_targetingPreviews.Count == 0) return;
        Destroy(_targetingPreviews[0]);
        _targetingPreviews.RemoveAt(0);
        RefreshAllStacks();
    }

    private Vector3 StackPosition(int index)
    {
        float x = 0f, y = 0f;
        for (int j = 0; j < index; j++)
        {
            float stepScale = Mathf.Pow(stackScaleFactor, j);
            x += stackOffsetX * stepScale;
            y += stackOffsetY * stepScale;
        }
        return new Vector3(x, y, 0f);
    }

    private void SpawnTrail(Transform origin)
    {
        if (trailPrefab == null || trailTarget == null) return;
        CameraController cam = Camera.main.GetComponent<CameraController>();
        bool isZoomed = cam != null && cam.IsZoomedIn;
        TrailAnimator trail = Instantiate(trailPrefab).GetComponent<TrailAnimator>();
        trail.Play(origin, trailTarget, isZoomed);
    }

    // Plus aucun appelant maintenant que les parties 1 (PushToAutoStack) et 3 (HandleEffectsBatch)
    // sont désactivées — la partie 2 (pile manuelle) ne l'a jamais utilisée.
    // private void ApplyMaterialToHologram(GameObject preview, Material material)
    // {
    //     if (material == null) return;
    //
    //     // Le popup contient un enfant "Hologram" (voir le prefab Effect_Triggered) — c'est LUI qui
    //     // reçoit le matériau de silhouette, pas le popup entier.
    //     Transform hologram = preview.transform.Find("CardPanel/Hologram");
    //     if (hologram == null) return;
    //     UnityEngine.UI.Image img = hologram.GetComponent<UnityEngine.UI.Image>();
    //     if (img != null) img.material = material;
    // }

    // Point d'Instantiate unique pour tous les popups Effect_Triggered du projet — appelé par les
    // 3 chemins (pile manuelle, batch de phase, effet auto isolé).
    private GameObject CreateTargetingPreview(PendingEffectSelection selection)
    {
        Debug.Log($"[CardPreviewUI] CreateTargetingPreview: {selection.Data?.EffectName} src={selection.SourceEntityID}");

        // Retrouve le GameObject en scène de la source de l'effet via son ID unique.
        GameObject sourceGO = IDHolder.GetGameObjectWithID(selection.SourceEntityID);
        if (sourceGO == null) return null; // source introuvable (déjà détruite, ID invalide -1, etc.)

        // Récupère la CardAsset depuis le manager de la source (créature ou bâtiment).
        CardAsset cardAsset = sourceGO.GetComponent<OneCreatureManager>()?.cardAsset
                        ?? sourceGO.GetComponent<OneBuildingManager>()?.cardAsset;
        if (cardAsset == null) return null;

        if (effectTriggeredPrefab == null) return null; // champ non assigné dans l'inspecteur

        // L'INSTANTIATE : seule ligne de tout le projet qui crée un popup Effect_Triggered.
        GameObject preview = Instantiate(effectTriggeredPrefab, targetingAnchor);
        preview.transform.localPosition = Vector3.zero;
        preview.transform.localRotation = Quaternion.identity;

        // Remplit le popup avec les infos de la carte + le texte de l'effet spécifique déclenché.
        OneCardManager manager = preview.GetComponent<OneCardManager>();
        manager.cardAsset = cardAsset;
        manager.owner = GlobalSettings.Instance != null ? GlobalSettings.Instance.localPlayer : null;
        manager.ReadEffectFromAsset(selection.Data.EffectName);
        previewingEffects = true; // bloque temporairement CardPreviewUI.Show() (aperçu de survol de carte)
        preview.SetActive(true);
        return preview;
    }


    private void HandleTargetingEnded()
    {
        StopAllCoroutines();
        // _autoEffectDismissCoroutine = null; // partie 1 désactivée — StopAllCoroutines() suffit
        _buildingStack = false;
        hoverArrow?.Hide();
        foreach (GameObject preview in _targetingPreviews)
            if (preview != null) Destroy(preview);
        _targetingPreviews.Clear();
        foreach (GameObject go in _autoEffectPreviews)
            if (go != null) Destroy(go);
        _autoEffectPreviews.Clear();
        previewingEffects = false;
    }

    public void Show(CardAsset asset, Vector2 mouseOffset, Player owner = null, int? attackOverride = null, int? healthOverride = null, int? maxHealthOverride = null, CreatureLogic sourceCreature = null, BuildingLogic sourceBuilding = null)
    {
        if(previewingEffects)
            return;
        Camera uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(),
            Input.mousePosition,
            uiCamera,
            out Vector2 localPoint
        );
        Vector2 previewPosition = localPoint + mouseOffset;
        _anchorRect.anchoredPosition = previewPosition;

        ShowPreview(asset, owner, attackOverride, healthOverride, maxHealthOverride, sourceCreature, sourceBuilding);
    }

    private void ShowPreview(CardAsset asset, Player owner = null, int? attackOverride = null, int? healthOverride = null, int? maxHealthOverride = null, CreatureLogic sourceCreature = null, BuildingLogic sourceBuilding = null)
    {
        GameObject prefabToUse = (asset.IsHero && heroCardPreviewPrefab != null) ? heroCardPreviewPrefab
            : (asset.Type == CardType.Action && actionCardPreviewPrefab != null) ? actionCardPreviewPrefab
            : cardPreviewPrefab;
        if (prefabToUse == null) return;

        if (currentPreview != null && currentPrefab != prefabToUse)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }

        if (currentPreview == null)
        {
            currentPreview = Instantiate(prefabToUse, previewAnchor);
            currentPreview.transform.localPosition = Vector3.zero;
            currentPreview.transform.localRotation = Quaternion.identity;
            currentPrefab = prefabToUse;
        }

        OneCardManager manager = currentPreview.GetComponent<OneCardManager>();
        manager.cardAsset = asset;
        manager.owner = owner;
        manager.sourceCreature = sourceCreature;
        manager.sourceBuilding = sourceBuilding;
        manager.ReadCardFromAsset();
        manager.OverrideStats(attackOverride, healthOverride, maxHealthOverride);
        ReminderTextManager.Instance?.BuildTooltips(BuildTooltipKeywords(asset));
        CardTooltipManager.Instance?.BuildCardTooltips(asset.ReferencedCards);

        currentPreview.SetActive(true);

        // mesurer/clamp à la taille finale (carte + tooltips), puis repartir de l'échelle réduite pour l'animation de pop-in
        currentPreview.transform.localScale = Vector3.one * previewScale;
        ClampPreviewToScreen((RectTransform)currentPreview.transform);

        // les tooltips sont ancrés au bord droit de la carte une fois sa position finale connue
        ReminderTextManager.Instance?.AnchorToCard(_anchorRect.anchoredPosition);
        CardTooltipManager.Instance?.AnchorToCard(_anchorRect.anchoredPosition);
        ReminderTextManager.Instance?.FadeIn();
        CardTooltipManager.Instance?.FadeIn();

        currentPreview.transform.localScale = Vector3.one * previewScale * 0.5f;
        currentPreview.transform.DOScale(Vector3.one * previewScale, 0.3f).SetEase(Ease.OutBack);
    }

    // Injecte automatiquement le keyword Melee/Ranged selon cardAsset.melee, sans avoir
    // à l'ajouter manuellement à la liste Keywords de chaque carte Unit/Hero.
    private List<Keyword> BuildTooltipKeywords(CardAsset asset)
    {
        List<Keyword> keywords = new List<Keyword>(asset.Keywords);

        if (asset.Type == CardType.Unit && GlobalSettings.Instance != null)
        {
            Keyword rowKeyword = asset.melee ? GlobalSettings.Instance.MeleeRowKeyword : GlobalSettings.Instance.RangedRowKeyword;
            if (rowKeyword != null)
                keywords.Insert(0, rowKeyword);
        }

        return keywords;
    }

    private void ClampPreviewToScreen(RectTransform previewRect)
    {
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        Vector2 shift = ComputeScreenShift(previewRect, cam);

        // Le bloc de tooltips va être ancré au bord droit de la carte (position finale + tooltipOffset) :
        // on vérifie s'il déborderait aussi de l'écran une fois la carte déplacée, et on cumule le shift.
        ReminderTextManager reminders = ReminderTextManager.Instance;
        if (reminders != null && reminders.TooltipRect.childCount > 0)
        {
            RectTransform tooltipRect = reminders.TooltipRect;
            Vector2 savedTooltipPos = tooltipRect.anchoredPosition;

            tooltipRect.anchoredPosition = _anchorRect.anchoredPosition + shift + reminders.TooltipOffset;
            shift += ComputeScreenShift(tooltipRect, cam);

            tooltipRect.anchoredPosition = savedTooltipPos; // AnchorToCard() fixera la position finale après coup
        }

        CardTooltipManager cardTooltips = CardTooltipManager.Instance;
        if (cardTooltips != null && cardTooltips.TooltipRect.childCount > 0)
        {
            RectTransform cardTooltipRect = cardTooltips.TooltipRect;
            Vector2 savedPos = cardTooltipRect.anchoredPosition;

            cardTooltips.AnchorToCard(_anchorRect.anchoredPosition + shift);
            shift += ComputeScreenShift(cardTooltipRect, cam);

            cardTooltipRect.anchoredPosition = savedPos; // AnchorToCard() final refixera la position après coup
        }

        if (shift != Vector2.zero)
            _anchorRect.anchoredPosition += shift;
    }

    private Vector2 ComputeScreenShift(RectTransform rect, Camera cam)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners); // 0 = bas-gauche, 2 = haut-droite

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        float shiftX = 0f;
        if (min.x < 0f) shiftX = -min.x;
        else if (max.x > Screen.width) shiftX = Screen.width - max.x;

        float shiftY = 0f;
        if (min.y < 0f) shiftY = -min.y;
        else if (max.y > Screen.height) shiftY = Screen.height - max.y;

        return new Vector2(shiftX, shiftY) / _canvas.scaleFactor;
    }

    // Partie 1 désactivée : plus aucun appelant (HandleAutoEffect commenté ci-dessus).
    // private void PushToAutoStack(PendingEffectSelection selection, Material materialOverride = null)
    // {
    //     // Instancie réellement le popup Effect_Triggered.
    //     GameObject preview = CreateTargetingPreview(selection);
    //     if (preview == null) return; // source introuvable dans la scène ou CardAsset manquante
    //
    //     // Applique le matériau hologramme si l'effet vient de l'adversaire.
    //     if (materialOverride != null)
    //         ApplyMaterialToHologram(preview, materialOverride);
    //
    //     // Position/échelle de départ (avant l'animation de RefreshAllStacks qui suit).
    //     preview.transform.localPosition = Vector3.zero;
    //     preview.transform.localScale    = Vector3.one * previewScale * stackDisplayScale * 0.5f;
    //     _autoEffectPreviews.Add(preview); // ajouté EN QUEUE de la pile (pas en tête)
    //
    //     // Animation du trail (traînée visuelle) partant de la source du popup.
    //     GameObject sourceGO = IDHolder.GetGameObjectWithID(selection.SourceEntityID);
    //     if (sourceGO != null) SpawnTrail(sourceGO.transform);
    //
    //     // Réanime toute la pile (positions, échelles dégressives, ordre d'affichage).
    //     RefreshAllStacks();
    //
    //     // Ne démarre le minuteur d'auto-disparition QUE s'il n'en existe pas déjà un en cours —
    //     // s'il y en a déjà un, il traitera ce nouveau popup à son tour quand il arrivera en tête.
    //     if (_autoEffectDismissCoroutine == null)
    //         _autoEffectDismissCoroutine = StartCoroutine(AutoDismissStack());
    // }

    private void RefreshAllStacks()
    {
        // Combine les deux piles existantes (manuelle + auto) pour les positionner ensemble —
        // en pratique l'une des deux est toujours vide (voir les gardes _batchActive /
        // _targetingPreviews.Count > 0 plus haut), donc "combined" ne contient que la pile
        // active du chemin en cours.
        var combined = new List<GameObject>();
        combined.AddRange(_targetingPreviews);
        combined.AddRange(_autoEffectPreviews);

        for (int i = 0; i < combined.Count; i++)
        {
            if (combined[i] == null) continue;
            // Échelle dégressive : chaque popup plus loin dans la pile est plus petit
            // (stackScaleFactor à la puissance i).
            float scale = previewScale * stackDisplayScale * Mathf.Pow(stackScaleFactor, i);
            combined[i].transform.DOLocalMove(StackPosition(i), 0.3f).SetEase(Ease.OutQuad);
            combined[i].transform.DOScale(
                Vector3.one * scale, 0.3f).SetEase(i == 0 ? Ease.OutBack : Ease.OutQuad);
        }

        // Sibling order : du fond vers l'avant, l'index 0 se retrouve en dernière position = rendu au-dessus
        for (int i = combined.Count - 1; i >= 0; i--)
            if (combined[i] != null) combined[i].transform.SetAsLastSibling();
    }

    private void ClearAutoStack()
    {
        // Coupe le minuteur en cours si un autre chemin (ex: début d'une sélection manuelle,
        // partie 2) doit reprendre la main immédiatement, sans attendre la fin naturelle
        // du délai d'affichage.
        // Partie 1 désactivée : plus de minuteur à couper (_autoEffectDismissCoroutine n'existe plus).
        // if (_autoEffectDismissCoroutine != null)
        // {
        //     StopCoroutine(_autoEffectDismissCoroutine);
        //     _autoEffectDismissCoroutine = null;
        // }
        foreach (GameObject go in _autoEffectPreviews)
            if (go != null) Destroy(go); // destruction immédiate, sans respecter autoEffectDisplayDuration
        _autoEffectPreviews.Clear();
        if (_targetingPreviews.Count == 0)
            previewingEffects = false;
    }

    // Partie 1 désactivée : plus aucun appelant (PushToAutoStack commenté ci-dessus).
    // // LE minuteur de la partie 1 : détruit les popups auto un par un, à intervalle
    // // autoEffectDisplayDuration, tant qu'il en reste dans la pile.
    // private IEnumerator AutoDismissStack()
    // {
    //     // Boucle tant qu'il reste des popups "auto" à afficher — chaque itération traite
    //     // le popup EN TÊTE de la pile (index 0), un par un.
    //     while (_autoEffectPreviews.Count > 0)
    //     {
    //         // C'EST ICI que la durée d'affichage est réellement appliquée : on attend
    //         // autoEffectDisplayDuration secondes (1.5s dans la scène) avant de détruire
    //         // le popup courant.
    //         yield return new WaitForSeconds(autoEffectDisplayDuration);
    //
    //         // Re-vérifie après l'attente : ClearAutoStack a pu vider la pile entre-temps.
    //         if (_autoEffectPreviews.Count == 0) break;
    //
    //         GameObject front = _autoEffectPreviews[0];
    //         _autoEffectPreviews.RemoveAt(0);
    //         if (front != null) Destroy(front); // destruction effective du popup
    //
    //         RefreshAllStacks(); // réanime la pile restante vers ses nouvelles positions
    //     }
    //
    //     // Plus aucun popup nulle part (ni manuel ni auto) → réautorise CardPreviewUI.Show()
    //     // (l'aperçu de survol de carte, bloqué tant que previewingEffects est vrai).
    //     if (_targetingPreviews.Count == 0)
    //         previewingEffects = false;
    //     _autoEffectDismissCoroutine = null; // libère la référence pour qu'un futur effet
    //                                          // puisse relancer une nouvelle coroutine
    // }


    public void Hide()
    {
        ReminderTextManager.Instance?.HideTooltips();
        CardTooltipManager.Instance?.HideTooltips();
        if (currentPreview != null)
            currentPreview.SetActive(false);
    }


}
