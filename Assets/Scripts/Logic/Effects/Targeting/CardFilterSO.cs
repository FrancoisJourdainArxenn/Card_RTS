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

    public bool Matches(CardAsset ca)
    {
        if (ca == null) return false;
        if (filterBySubType && ca.subType != requiredSubType) return false;
        if (filterByName && ca.Name != requiredName) return false;
        return true;
    }
}
