using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using DG.Tweening;

// Vit sur l'objet "DeckList" (parent de DeckList_Panel et AvailableDecks). DeckList lui-meme
// reste toujours actif (il porte aussi AvailableDecks, independant du hover) : c'est
// DeckList_Panel qu'on active/desactive pour le hover-preview, pas DeckList entier.
public class DeckListPanelController : MonoBehaviour
{
    public static DeckListPanelController Instance { get; private set; }

    public bool IsChoosingPool => availableDecksPanel != null && availableDecksPanel.activeSelf;

    // Permet aux CardPoolSlot (origine et candidats) de desactiver leur tooltip generique
    // pendant que ce panneau est ouvert, puisque le survol affiche deja DeckList_Panel.
    public static event System.Action<bool> OnChoosingPoolChanged;

    [SerializeField] private RectTransform content;
    [SerializeField] private GameObject deckListPanel;
    [SerializeField] private TMP_Text deckNameText;
    [SerializeField] private CardSummaryDeckListVisual cardSummaryPrefab;
    [SerializeField] private RectTransform canvasRectTransform;
    [SerializeField] private RectTransform titleRect;

    [Header("Choose Pool Panel")]
    [SerializeField] private GameObject availableDecksPanel;
    [SerializeField] private GameObject clickOutsideBackdrop;
    [SerializeField] private CardPoolSlot mainDeckSlotPrefab;
    [SerializeField] private CardPoolSlot secondDeckSlotPrefab;

    [Header("Choose Pool Panel - Row 1 (recap)")]
    [SerializeField] private Image recapHeroImage;
    [SerializeField] private TMP_Text heroNameText;
    [SerializeField] private CardPoolSlot recapMainSlot;
    [SerializeField] private CardPoolSlot recapSecondSlot;

    [Header("Choose Pool Panel - Row 2 / Row 3 (choix)")]
    [SerializeField] private RectTransform mainPoolContent;
    [SerializeField] private RectTransform secondPoolContent;

    [Header("Anim")]
    [SerializeField] private float panelScaleDuration = 0.2f;

    private Canvas canvas;
    private RectTransform rectTransform;
    private readonly List<CardSummaryDeckListVisual> spawnedEntries = new List<CardSummaryDeckListVisual>();
    private readonly List<CardPoolSlot> spawnedPoolEntries = new List<CardPoolSlot>();

    // Hero (sur HeroPortrait) qui a ouvert AvailableDecks - remis a zero a chaque fermeture du panneau.
    private HeroPortrait originatingHero;

    private void Awake()
    {
        Instance = this;
        rectTransform = GetComponent<RectTransform>();
        canvas = canvasRectTransform.GetComponent<Canvas>();

        // Mise en place initiale sans animation (juste apres le chargement de la scene, avant
        // toute interaction) : eviter un flash visible si les panels sont laisses actifs dans l'editeur.
        SetPanelScaleInstant(deckListPanel, 0f);
        deckListPanel.SetActive(false);
        SetPanelScaleInstant(availableDecksPanel, 0f);
        availableDecksPanel.SetActive(false);
        if (clickOutsideBackdrop != null)
            clickOutsideBackdrop.SetActive(false);
    }

    private static void SetPanelScaleInstant(GameObject panel, float scaleX)
    {
        panel.transform.localScale = new Vector3(scaleX, 1f, 1f);
    }

    // Scale en X uniquement (0 -> 1), la hauteur ne bouge pas. No-op si deja ouvert : ShowDeckList
    // (CardPoolSO) est rappele a chaque survol d'un candidat different dans AvailableDecks, on ne
    // veut pas relancer l'animation a chaque changement de contenu, seulement a l'ouverture reelle.
    private void PlayOpenAnimation(GameObject panel)
    {
        if (panel.activeSelf)
            return;

        Transform t = panel.transform;
        t.DOKill();
        t.localScale = new Vector3(0f, 1f, 1f);
        panel.SetActive(true);
        t.DOScaleX(1f, panelScaleDuration);
    }

    private void PlayCloseAnimation(GameObject panel)
    {
        Transform t = panel.transform;
        t.DOKill();
        t.DOScaleX(0f, panelScaleDuration).OnComplete(() => panel.SetActive(false));
    }

    public void ShowDeckList(DeckSO deck)
    {
        if (deck == null)
        {
            Debug.Log($"[DeckList] Fermeture : ShowDeckList appele avec deck == null (frame {Time.frameCount}).");
            PlayCloseAnimation(deckListPanel);
            return;
        }

        Debug.Log($"[DeckList] Ouverture : {deck.deckName} (frame {Time.frameCount}).");
        if (deckNameText != null)
            deckNameText.text = deck.deckName;
        ShowCardList(deck.cards, positionAtMouse: true);
    }

    public void ShowDeckList(CardPoolSO pool)
    {
        if (pool == null)
        {
            PlayCloseAnimation(deckListPanel);
            return;
        }

        if (deckNameText != null)
            deckNameText.text = pool.poolName;

        // Pas de repositionnement sous la souris ici : ce chemin est declenche par le survol
        // d'un CardPoolSlot (souvent petit), recentrer le panneau dessus le recouvrirait et
        // casserait son propre raycast (boucle Enter/Exit). DeckList_Panel garde sa position
        // d'origine dans la scene.
        ShowCardList(pool.cards, positionAtMouse: false);
    }

    private void ShowCardList(List<CardAsset> cards, bool positionAtMouse)
    {
        ClearEntries();

        foreach (CardAsset card in cards)
            SpawnEntry(card);

        PlayOpenAnimation(deckListPanel);
        if (positionAtMouse)
            PositionAtMouse();
    }

    private void SpawnEntry(CardAsset card)
    {
        CardSummaryDeckListVisual entry = Instantiate(cardSummaryPrefab, content);
        entry.SetCard(card);
        spawnedEntries.Add(entry);
    }

    private void ClearEntries()
    {
        foreach (CardSummaryDeckListVisual entry in spawnedEntries)
            Destroy(entry.gameObject);
        spawnedEntries.Clear();
    }

    public void HideDeckList(string reason)
    {
        Debug.Log($"[DeckList] Fermeture : {reason} (frame {Time.frameCount}).");
        PlayCloseAnimation(deckListPanel);
    }

    public void ShowPoolChoices(FactionAsset faction, HeroPortrait originHero)
    {
        originatingHero = originHero;

        ClearPoolEntries();

        foreach (CardPoolSO pool in faction.mainCardPools)
            SpawnPoolEntry(pool, mainDeckSlotPrefab, mainPoolContent);
        foreach (CardPoolSO pool in faction.secondCardPools)
            SpawnPoolEntry(pool, secondDeckSlotPrefab, secondPoolContent);

        RefreshRecap();
        RefreshPoolAvailability();

        PlayOpenAnimation(availableDecksPanel);
        if (clickOutsideBackdrop != null)
            clickOutsideBackdrop.SetActive(true);

        OnChoosingPoolChanged?.Invoke(true);
    }

    public void HidePoolChoices()
    {
        PlayCloseAnimation(availableDecksPanel);
        if (clickOutsideBackdrop != null)
            clickOutsideBackdrop.SetActive(false);
        HideDeckList("HidePoolChoices");

        originatingHero = null;

        OnChoosingPoolChanged?.Invoke(false);
    }

    // Reflete le hero, le MainDeck et le SecondDeck actuellement selectionnes pour originatingHero
    // (Row 1) : appele a l'ouverture du panneau, puis a chaque nouveau choix (ApplyPoolChoice).
    private void RefreshRecap()
    {
        if (originatingHero == null)
            return;

        if (recapHeroImage != null)
            recapHeroImage.sprite = originatingHero.PortraitSprite;
        if (heroNameText != null)
            heroNameText.text = originatingHero.deck != null && originatingHero.deck.heroCard != null
                ? originatingHero.deck.heroCard.Name
                : string.Empty;
        if (recapMainSlot != null)
            recapMainSlot.SetCardPool(originatingHero.deck != null ? originatingHero.deck.mainPool : null);
        if (recapSecondSlot != null)
            recapSecondSlot.SetCardPool(originatingHero.deck != null ? originatingHero.deck.secondPool : null);
    }

    // Applique immediatement un choix de pool (clic sur une entree candidate de Row 2/Row 3) sur
    // le DeckSO du hero d'origine, et rafraichit le recap de Row 1 en consequence.
    public void ApplyPoolChoice(CardPoolSlot.SlotKind kind, CardPoolSO pool)
    {
        if (originatingHero == null)
            return;

        // Le DeckSO du hero (asset preset, retrouve par reference dans MenuRegistry.decks pour
        // la sync reseau par index) porte desormais directement les deux pools choisis : ce choix
        // devient ainsi le deck reellement utilise au lancement de partie (voir DeckSO.cards).
        if (originatingHero.deck != null)
        {
            if (kind == CardPoolSlot.SlotKind.Main)
                originatingHero.deck.mainPool = pool;
            else
                originatingHero.deck.secondPool = pool;

            // Certains pools Main/Second partagent volontairement le meme poolName : impossible
            // d'avoir les deux cotes sur "la meme" identite en meme temps. Si l'autre cote portait
            // deja ce nom, il est retire.
            ClearConflictingSelection(kind, pool);

            // Ne rafraichit FullDeckList_Panel que si le hero dont le pool vient de changer est
            // bien celui actuellement affiche (selectionne) — sinon la liste visible n'est pas concernee.
            if (originatingHero.deck == HeroPortrait.SelectedDeck && FullDeckListPanelController.Instance != null)
                FullDeckListPanelController.Instance.ShowFullDeck(originatingHero.deck);
        }

        RefreshRecap();
        RefreshPoolAvailability();
    }

    private void ClearConflictingSelection(CardPoolSlot.SlotKind kind, CardPoolSO pool)
    {
        if (pool == null || originatingHero.deck == null)
            return;

        if (kind == CardPoolSlot.SlotKind.Main)
        {
            if (originatingHero.deck.secondPool != null && originatingHero.deck.secondPool.poolName == pool.poolName)
                originatingHero.deck.secondPool = null;
        }
        else
        {
            if (originatingHero.deck.mainPool != null && originatingHero.deck.mainPool.poolName == pool.poolName)
                originatingHero.deck.mainPool = null;
        }
    }

    // Grise/rend non cliquables les candidats de Row 2/Row 3 dont le poolName correspond a celui
    // deja choisi de l'autre cote (Main <-> Second) : ces deux pools ne peuvent pas coexister.
    private void RefreshPoolAvailability()
    {
        if (originatingHero == null || originatingHero.deck == null)
            return;

        string mainName = originatingHero.deck.mainPool != null ? originatingHero.deck.mainPool.poolName : null;
        string secondName = originatingHero.deck.secondPool != null ? originatingHero.deck.secondPool.poolName : null;

        foreach (CardPoolSlot entry in spawnedPoolEntries)
        {
            string conflictingName = entry.Kind == CardPoolSlot.SlotKind.Main ? secondName : mainName;
            bool blocked = entry.cardPool != null && conflictingName != null && entry.cardPool.poolName == conflictingName;
            entry.SetSelectable(!blocked);
        }
    }

    // A cabler sur le OnClick de Confirm_Button : les choix s'appliquent desormais immediatement
    // (ApplyPoolChoice), ce bouton ne fait plus que fermer le panneau.
    public void ConfirmPoolChoice()
    {
        HidePoolChoices();
    }

    private void SpawnPoolEntry(CardPoolSO pool, CardPoolSlot prefab, RectTransform parent)
    {
        CardPoolSlot entry = Instantiate(prefab, parent);
        entry.SetCardPool(pool);
        spawnedPoolEntries.Add(entry);
    }

    private void ClearPoolEntries()
    {
        foreach (CardPoolSlot entry in spawnedPoolEntries)
            Destroy(entry.gameObject);
        spawnedPoolEntries.Clear();
    }

    // Colle le bord gauche du panel sous la souris (centre verticalement dessus),
    // puis le ramene entierement a l'ecran si besoin.
    private void PositionAtMouse()
    {
        RectTransform parentRect = rectTransform.parent as RectTransform;
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, Input.mousePosition, cam, out Vector2 localPoint);

        float halfWidth = rectTransform.rect.width * 0.5f;
        rectTransform.anchoredPosition = localPoint + new Vector2(halfWidth, 0f);

        Vector2 shift = ComputeScreenShift(rectTransform, titleRect);
        if (shift != Vector2.zero)
            rectTransform.anchoredPosition += shift;
    }

    // extraRect permet d'inclure des elements qui debordent volontairement du rect
    // principal (ex: Deck_Title qui depasse au-dessus de Deck_ListPanel) dans le calcul
    // du clamp a l'ecran.
    private Vector2 ComputeScreenShift(RectTransform rect, RectTransform extraRect = null)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners); // 0 = bas-gauche, 2 = haut-droite

        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        if (extraRect != null)
        {
            Vector3[] extraCorners = new Vector3[4];
            extraRect.GetWorldCorners(extraCorners);
            Vector2 extraMin = RectTransformUtility.WorldToScreenPoint(cam, extraCorners[0]);
            Vector2 extraMax = RectTransformUtility.WorldToScreenPoint(cam, extraCorners[2]);
            min = Vector2.Min(min, extraMin);
            max = Vector2.Max(max, extraMax);
        }

        float shiftX = 0f;
        if (min.x < 0f) shiftX = -min.x;
        else if (max.x > Screen.width) shiftX = Screen.width - max.x;

        float shiftY = 0f;
        if (min.y < 0f) shiftY = -min.y;
        else if (max.y > Screen.height) shiftY = Screen.height - max.y;

        return new Vector2(shiftX, shiftY) / canvas.scaleFactor;
    }

    public static void ShowDeckList_Static(DeckSO deck)
    {
        Instance.ShowDeckList(deck);
    }

    public static void HideDeckList_Static()
    {
        Debug.Log($"[DeckList] HideDeckList_Static appele depuis un script externe.\n{System.Environment.StackTrace}");
        Instance.HideDeckList("HideDeckList_Static (appel externe)");
    }

}
