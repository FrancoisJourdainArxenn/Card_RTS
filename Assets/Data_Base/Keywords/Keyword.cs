using UnityEngine;

[CreateAssetMenu(fileName = "KeywordEntry", menuName = "Keywords/KeywordDataBase")]
public class Keyword : ScriptableObject
{
    public string displayName;
    [TextArea (2,5)]
    public string description;
    public Sprite icon;
}
