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

    public bool Matches(CardAsset ca)
    {
        if (ca == null) return false;
        if (filterBySubType && ca.subType != requiredSubType) return false;
        // ca.Name (le champ custom) est vide sur la plupart des CardAsset (ex: Roach, Egg Sack),
        // mais c'est justement lui qui porte l'identité partagée entre une carte et ses tokens
        // (ex: "Raptor Token" a Name="Raptor" pour matcher son unité source malgré un m_Name
        // différent). On priorise donc Name quand il est rempli, avec ca.name en repli sinon.
        if (filterByName)
        {
            string effectiveName = string.IsNullOrEmpty(ca.Name) ? ca.name : ca.Name;
            if (effectiveName != requiredName) return false;
        }
        if (filterByCelerity && ca.Celerity != requiredCelerity) return false;
        return true;
    }
}
