using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChooseOneManager : MonoBehaviour
{
    public static ChooseOneManager Instance;

    [Header("UI References (à câbler dans l'Inspector)")]
    public GameObject panelRoot;    // overlay plein écran + rangée de cartes, inactif par défaut
    public Transform cardContainer; // parent avec un HorizontalLayoutGroup

    [Header("Card Prefabs (PAS les prefabs de main — les mêmes que CardPreviewUI)")]
    public GameObject cardPreviewPrefab;      // Card_Unit_Preview — créatures
    public GameObject heroCardPreviewPrefab;  // Hero_Preview
    public GameObject spellCardPreviewPrefab; // Card_Action_Preview — sorts (CardType.Action)

    [Header("Bouton Hide/Show (HORS de panelRoot, sinon il disparaît avec lui)")]
    public GameObject toggleButtonRoot;
    public TMP_Text toggleButtonLabel;

    private class PendingChoice
    {
        public Player Caster;
        public List<CardAsset> Offered;
        public int SourceEntityID;
        public int EffectIndex;
    }

    private readonly Queue<PendingChoice> _queue = new Queue<PendingChoice>();
    private PendingChoice _current;
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();
    private bool _isHidden;

    public bool AnyPending => _current != null || _queue.Count > 0;

    public bool HasPendingChoice(Player player) =>
        (_current != null && _current.Caster == player) || _queue.Any(p => p.Caster == player);

    void Awake()
    {
        Instance = this;
        RefreshVisibility();
    }

    /// <summary>Appelé par ChooseOneSO.Execute (solo) ou GameNetworkManager.ChooseOneOfferClientRpc (réseau).</summary>
    public void BeginOffer(Player caster, List<CardAsset> offered, int sourceEntityID, int effectIndex)
    {
        if (GlobalSettings.Instance == null || caster != GlobalSettings.Instance.localPlayer)
            return; // seul le client du joueur concerné affiche ce choix

        bool wasIdle = _current == null;

        _queue.Enqueue(new PendingChoice
        {
            Caster = caster,
            Offered = offered,
            SourceEntityID = sourceEntityID,
            EffectIndex = effectIndex
        });

        if (wasIdle)
        {
            // Laisse le temps de voir le VFX déclenché sur la source (ChooseOneSO.QueueSourceVfx)
            // avant que le panneau ne s'ouvre et n'attire l'attention du joueur ailleurs.
            float delay = TurnManager.Instance != null ? TurnManager.Instance.EffectSequenceDelay : 1.5f;
            StartCoroutine(ShowNextDelayed(delay));
        }
    }

    private System.Collections.IEnumerator ShowNextDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        ShowNext();
    }

    void ShowNext()
    {
        if (_queue.Count == 0)
        {
            _current = null;
            _isHidden = false;
            RefreshVisibility();
            GlobalSettings.Instance?.RefreshEndPhaseButtons();
            return;
        }

        _current = _queue.Dequeue();
        _isHidden = false;
        RefreshVisibility();

        foreach (GameObject go in _spawnedCards)
            if (go != null) Destroy(go);
        _spawnedCards.Clear();

        foreach (CardAsset candidate in _current.Offered)
        {
            GameObject prefab = candidate.IsHero && heroCardPreviewPrefab != null
                ? heroCardPreviewPrefab
                : candidate.Type == CardType.Action && spellCardPreviewPrefab != null
                    ? spellCardPreviewPrefab
                    : cardPreviewPrefab;
            GameObject cardGO = Instantiate(prefab, cardContainer);
            cardGO.transform.localPosition = Vector3.zero;
            cardGO.transform.localRotation = Quaternion.identity;
            cardGO.transform.localScale = Vector3.one;
            cardGO.SetActive(true);

            OneCardManager manager = cardGO.GetComponent<OneCardManager>();
            manager.cardAsset = candidate;
            manager.owner = _current.Caster;
            manager.ReadCardFromAsset();

            // Les prefabs de preview n'ont le Raycast Target que sur les textes (voulu pour CardPreviewUI,
            // qui ne doit pas bloquer le plateau) : ici toute la carte doit être cliquable. Le glow est
            // exclu pour que la zone de survol ne s'agrandisse pas quand il s'allume.
            foreach (Graphic graphic in cardGO.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = graphic != manager.CardFaceGlowImage;

            ChooseOneCardClickHandler click = cardGO.AddComponent<ChooseOneCardClickHandler>();
            click.Init(this, candidate, manager.CardFaceGlowImage);

            _spawnedCards.Add(cardGO);
        }

        GlobalSettings.Instance?.RefreshEndPhaseButtons();
    }

    /// <summary>Appelé par ChooseOneCardClickHandler quand le joueur clique une des cartes offertes.</summary>
    public void OnCardPicked(CardAsset chosen)
    {
        if (_current == null) return;
        PendingChoice resolved = _current;

        foreach (GameObject go in _spawnedCards)
            if (go != null) Destroy(go);
        _spawnedCards.Clear();
        _current = null;
        RefreshVisibility();

        if (!NetworkSessionData.IsNetworkSession)
        {
            resolved.Caster.GetACardNotFromDeck(chosen);
            ShowNext();
            return;
        }

        ChooseOneSO so = EffectRegistry.GetChooseOneSO(resolved.SourceEntityID, resolved.EffectIndex);
        int poolIndex = so != null ? so.CardPool.IndexOf(chosen) : -1;
        int playerIndex = System.Array.IndexOf(Player.Players, resolved.Caster);

        if (poolIndex >= 0 && playerIndex >= 0)
            GameNetworkManager.Instance.SubmitChooseOnePickServerRpc(playerIndex, resolved.SourceEntityID, resolved.EffectIndex, poolIndex);

        ShowNext();
    }

    /// <summary>Branché sur le OnClick du bouton Hide/Show.</summary>
    public void ToggleVisibility()
    {
        if (_current == null) return;
        _isHidden = !_isHidden;
        RefreshVisibility();
    }

    void RefreshVisibility()
    {
        bool hasChoice = _current != null;
        if (panelRoot != null) panelRoot.SetActive(hasChoice && !_isHidden);
        if (toggleButtonRoot != null) toggleButtonRoot.SetActive(hasChoice);
        if (toggleButtonLabel != null) toggleButtonLabel.text = _isHidden ? "Show" : "Hide";
    }
}
