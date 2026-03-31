using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class FindMissingScriptsTool : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("Tools/Missing Scripts/Find Missing Scripts")]

    private static void FindMissingScriptsInSceneMenuItem()
    {
        foreach (GameObject gameObject in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Component[] components = gameObject.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                {

                }
            }
        }
    }

        [MenuItem("Tools/Missing Scripts/Show Hidden Missing Scripts In Scene")]

    private static void ShowHiddenMissingScriptsInSceneMenuItem()
    {
        foreach (GameObject gameObject in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Component[] components = gameObject.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                {
                    if(gameObject.hideFlags.HasFlag(HideFlags.HideInHierarchy))
                    {
                        gameObject.hideFlags = gameObject.hideFlags & ~HideFlags.HideInHierarchy;
                        Debug.Log($"Missing component on {gameObject.name}", gameObject);
                        break;
                    }
                }
            }
        }
    }
#endif
}
