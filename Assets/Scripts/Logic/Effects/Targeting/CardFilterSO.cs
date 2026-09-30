using UnityEngine;

[CreateAssetMenu(menuName = "Effects/Card Filter")]
public class CardFilterSO : ScriptableObject
{
    [Header("Sub Type")]
    public bool filterBySubType;
    public SubType requiredSubType;

    [Header("Name")]
    public bool filterByName;
    public string requiredName;

    [Header("Celerity")]
    public bool filterByCelerity;
    public bool requiredCelerity = true;

    [Header("Flying")]
    public bool filterByFlying;
    public bool requiredFlying = true;

    [Header("Melee / Ranged")]
    public bool filterByMelee;
    [Tooltip("Coché = Melee, décoché = Ranged.")]
    public bool requiredMelee = true;

    [Header("Home Base")]
    [Tooltip("Unité-base uniquement (voir CreatureLogic.IsHomeUnit) : carte cochée CountsAsHomeBase, ou, une fois en jeu, l'unité de départ du deck (CardPoolSO.homeUnit).")]
    public bool filterByHomeBase;

    // Carte seule (en main, ou créature pas encore créée) : l'unité de départ du deck n'y est pas
    // reconnue comme base, ce statut ne lui étant accordé qu'en jeu — voir Matches(CreatureLogic).
    public bool Matches(CardAsset ca)
    {
        if (!MatchesCard(ca)) return false;
        if (filterByHomeBase && !ca.CountsAsHomeBase) return false;
        return true;
    }

    // Créature en jeu : voit aussi ce qui n'existe qu'à l'exécution, comme le statut d'unité-base
    // accordé par Player.SpawnHomeUnitIfConfigured à une carte sans CountsAsHomeBase.
    public bool Matches(CreatureLogic creature)
    {
        if (creature == null || !MatchesCard(creature.ca)) return false;
        if (filterByHomeBase && !creature.IsHomeUnit) return false;
        return true;
    }

    private bool MatchesCard(CardAsset ca)
    {
        if (ca == null) return false;
        if (filterBySubType && ca.subType != requiredSubType) return false;
        if (filterByName && EffectiveName(ca) != requiredName) return false;
        if (filterByCelerity && ca.Celerity != requiredCelerity) return false;
        if (filterByFlying && ca.Flying != requiredFlying) return false;
        if (filterByMelee && ca.melee != requiredMelee) return false;
        return true;
    }

    // ca.Name (le champ custom) est vide sur la plupart des CardAsset (ex: Roach, Egg Sack),
    // mais c'est justement lui qui porte l'identité partagée entre une carte et ses tokens
    // (ex: "Raptor Token" a Name="Raptor" pour matcher son unité source malgré un m_Name
    // différent). On priorise donc Name quand il est rempli, avec ca.name en repli sinon.
    public static string EffectiveName(CardAsset ca) => string.IsNullOrEmpty(ca.Name) ? ca.name : ca.Name;
}
