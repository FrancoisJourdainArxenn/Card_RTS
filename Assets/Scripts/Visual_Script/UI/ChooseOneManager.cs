using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ChooseOneManager : MonoBehaviour
{
    public static ChooseOneManager Instance;

    [Header("UI References (à câbler dans l'Inspector)")]
    public GameObject panelRoot;    // overlay plein écran + rangée de cartes, inactif par défaut
    public Transform cardContainer; // parent avec un HorizontalLayoutGroup

    [Header("Card Prefabs (PAS les prefabs de main — les mêmes que CardPreviewUI)")]
    public GameObject cardPreviewPrefab;      // Card_Unit_Preview — créatures/bâtiments
    public GameObject heroCardPreviewPrefab;  // Hero_Preview
    public GameObject spellCardPreviewPrefab; // Card_Action_Preview — sorts (CardType.Action)

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

    public bool AnyPending => _current != null || _queue.Count > 0;

    public bool HasPendingChoice(Player player) =>
        (_current != null && _current.Caster == player) || _queue.Any(p => p.Caster == player);

    void Awake()
    {
        Instance = this;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    /// <summary>Appelé par ChooseOneSO.Execute (solo) ou GameNetworkManager.ChooseOneOfferClientRpc (réseau).</summary>
    public void BeginOffer(Player caster, List<CardAsset> offered, int sourceEntityID, int effectIndex)
    {
        if (GlobalSettings.Instance == null || caster != GlobalSettings.Instance.localPlayer)
            return; // seul le client du joueur concerné affiche ce choix

        _queue.Enqueue(new PendingChoice
        {
            Caster = caster,
            Offered = offered,
            SourceEntityID = sourceEntityID,
            EffectIndex = effectIndex
        });

        if (_current == null)
            ShowNext();
    }

    void ShowNext()
    {
        if (_queue.Count == 0)
        {
            _current = null;
            if (panelRoot != null) panelRoot.SetActive(false);
            GlobalSettings.Instance?.RefreshEndPhaseButtons();
            return;
        }

        _current = _queue.Dequeue();
        if (panelRoot != null) panelRoot.SetActive(true);

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

            ChooseOneCardClickHandler click = cardGO.AddComponent<ChooseOneCardClickHandler>();
            click.Init(this, candidate);

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
        if (panelRoot != null) panelRoot.SetActive(false);
        _current = null;

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
}
